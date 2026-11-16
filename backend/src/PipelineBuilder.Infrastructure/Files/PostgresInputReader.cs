using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Npgsql;
using Polly;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Infrastructure.Files;

internal static class PostgresInputReader
{
    private const int ConnectionTimeoutSeconds = 15;
    private const int CommandTimeoutSeconds = 60;
    private const int DiscoverySampleRows = 100;
    private const string AllowedHostsSetting = "PipelinePostgres__AllowedHosts";

    public static async Task<ColumnRecord[]> Discover(PostgresInputStep postgres, ResiliencePipeline resilience, CancellationToken ct)
    {
        try
        {
            await ValidateHost(postgres.Host, postgres.Id, ct);
            return await resilience.ExecuteAsync(token => DiscoverOnce(postgres, token), ct);
        }
        catch (PipelineDataException) { throw; }
        catch (Exception ex) when (ex is NpgsqlException or SocketException or TimeoutException or ArgumentException or FormatException)
        {
            throw new PipelineDataException("PostgreSQL discovery failed. Check the connection, SSL mode, read-only access and query.", postgres.Id, ex);
        }
    }

    private static async ValueTask<ColumnRecord[]> DiscoverOnce(PostgresInputStep postgres, CancellationToken ct)
    {
        await using var connection = CreateConnection(postgres);
        await connection.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await SetReadOnly(connection, transaction, ct);
        await using var command = CreateCommand(postgres, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleResult, ct);
        var columns = await reader.GetColumnSchemaAsync(ct);
        if (columns.Count is < 1 or > 200) throw new PipelineDataException("PostgreSQL query must return 1 to 200 columns.", postgres.Id);
        var names = Enumerable.Range(0, columns.Count).Select(reader.GetName).ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new PipelineDataException("PostgreSQL query columns must have unique, nonempty names; add aliases where needed.", postgres.Id);
        var nullable = columns.Select(column => column.AllowDBNull is not false).ToArray();
        var hasNull = new bool[columns.Count];
        var samples = 0;
        while (samples < DiscoverySampleRows && await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            samples++;
            for (var index = 0; index < columns.Count; index++) hasNull[index] |= reader.IsDBNull(index);
        }
        return columns.Select((column, index) => MapColumn(names[index], reader.GetDataTypeName(index), column,
            hasNull[index] || samples == 0 && nullable[index])).ToArray();
    }

    public static async IAsyncEnumerable<Dictionary<string, object?>> Read(PostgresInputStep postgres, ResiliencePipeline resilience,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await ValidateHost(postgres.Host, postgres.Id, ct);
        PostgresSession session;
        try
        {
            session = await resilience.ExecuteAsync(token => OpenSession(postgres, token), ct);
        }
        catch (PipelineDataException) { throw; }
        catch (Exception ex) when (ex is NpgsqlException or SocketException or TimeoutException or ArgumentException)
        {
            throw new PipelineDataException("PostgreSQL connection or query setup failed. Check the host, credentials and SSL mode.", postgres.Id, ex);
        }
        await using (session)
        {
            var reader = session.Reader;
            var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
            if (reader.FieldCount != postgres.Schema.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
                !postgres.Schema.All(column => names.Contains(column.Name, StringComparer.Ordinal)))
                throw new PipelineDataException("PostgreSQL result columns do not match the confirmed schema.", postgres.Id);

            long rowNumber = 0;
            var hasRow = session.HasCurrentRow;
            while (hasRow)
            {
                ct.ThrowIfCancellationRequested();
                rowNumber++;
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    var column = postgres.Schema.Single(item => item.Name == names[index]);
                    var value = reader.IsDBNull(index) ? null : ToText(reader.GetValue(index), column.Type);
                    row[column.Name] = PipelineFiles.ConvertValue(value, column, null, rowNumber, postgres.Id);
                }
                // shortcut: failures after rows stream abort this run rather than retrying from row one and duplicating output; restart the run to replay it.
                yield return row;
                try { hasRow = await reader.ReadAsync(ct); }
                catch (NpgsqlException ex) { throw new PipelineDataException("PostgreSQL query failed while reading rows.", postgres.Id, ex); }
            }
        }
    }

    private static async ValueTask<PostgresSession> OpenSession(PostgresInputStep postgres, CancellationToken ct)
    {
        var connection = CreateConnection(postgres);
        NpgsqlTransaction? transaction = null;
        NpgsqlCommand? command = null;
        NpgsqlDataReader? reader = null;
        var transferred = false;
        try
        {
            await connection.OpenAsync(ct);
            transaction = await connection.BeginTransactionAsync(ct);
            await SetReadOnly(connection, transaction, ct);
            command = CreateCommand(postgres, connection, transaction);
            reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult, ct);
            var hasCurrentRow = await reader.ReadAsync(ct);
            transferred = true;
            return new PostgresSession(connection, transaction, command, reader, hasCurrentRow);
        }
        finally
        {
            if (!transferred)
            {
                if (reader is not null) await reader.DisposeAsync();
                if (command is not null) await command.DisposeAsync();
                if (transaction is not null) await transaction.DisposeAsync();
                await connection.DisposeAsync();
            }
        }
    }

    private static async Task SetReadOnly(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static NpgsqlCommand CreateCommand(PostgresInputStep postgres, NpgsqlConnection connection, NpgsqlTransaction transaction) =>
        new(postgres.Query, connection, transaction) { CommandTimeout = CommandTimeoutSeconds };

    private sealed class PostgresSession(NpgsqlConnection connection, NpgsqlTransaction transaction,
        NpgsqlCommand command, NpgsqlDataReader reader, bool hasCurrentRow) : IAsyncDisposable
    {
        public NpgsqlDataReader Reader { get; } = reader;
        public bool HasCurrentRow { get; } = hasCurrentRow;

        public async ValueTask DisposeAsync()
        {
            try { await Reader.DisposeAsync(); }
            finally
            {
                try { await command.DisposeAsync(); }
                finally
                {
                    try { await transaction.DisposeAsync(); }
                    finally { await connection.DisposeAsync(); }
                }
            }
        }
    }

    private static NpgsqlConnection CreateConnection(PostgresInputStep postgres)
    {
        try { return new NpgsqlConnection(ConnectionString(postgres)); }
        catch (ArgumentException ex) { throw new PipelineDataException("PostgreSQL connection properties are invalid.", postgres.Id, ex); }
    }

    private static string ConnectionString(PostgresInputStep postgres)
    {
        if (!int.TryParse(postgres.Port, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535 ||
            !Enum.TryParse<SslMode>(postgres.SslMode, false, out var sslMode))
            throw new PipelineDataException("PostgreSQL port or SSL mode is invalid.", postgres.Id);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = postgres.Host,
            Port = port,
            Database = postgres.Database,
            Username = Secret(postgres.Username, postgres.Id),
            Password = Secret(postgres.Password, postgres.Id),
            SslMode = sslMode,
            Timeout = ConnectionTimeoutSeconds,
            CommandTimeout = CommandTimeoutSeconds,
            ApplicationName = "PipelineBuilder"
        };
        return builder.ConnectionString;
    }

    private static async Task ValidateHost(string host, string blockId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Contains(',') || host.Contains('/') || host.Contains('\\'))
            throw new PipelineDataException("Use one PostgreSQL host name or IP address.", blockId);
        var allowed = (Environment.GetEnvironmentVariable(AllowedHostsSetting) ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowed.Contains(host, StringComparer.OrdinalIgnoreCase)) return;
        IPAddress[] addresses;
        try { addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct); }
        catch (SocketException ex) { throw new PipelineDataException("PostgreSQL host could not be resolved.", blockId, ex); }
        if (addresses.Length == 0 || addresses.Any(IsPrivate))
            throw new PipelineDataException($"PostgreSQL host resolves to a private or local address; add it to {AllowedHostsSetting} to allow it explicitly.", blockId);
    }

    private static bool IsPrivate(IPAddress ip) => ip.IsIPv4MappedToIPv6 && IsPrivate(ip.MapToIPv4()) ||
        IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || ip.Equals(IPAddress.IPv6Any) ||
        ip.MapToIPv6().GetAddressBytes()[0] is 0xfc or 0xfd ||
        ip.AddressFamily == AddressFamily.InterNetwork && (ip.GetAddressBytes()[0] is 0 or 10 or 127 or >= 224 ||
            ip.GetAddressBytes()[0] == 169 && ip.GetAddressBytes()[1] == 254 ||
            ip.GetAddressBytes()[0] == 100 && ip.GetAddressBytes()[1] is >= 64 and <= 127 ||
            ip.GetAddressBytes()[0] == 192 && ip.GetAddressBytes()[1] == 168 ||
            ip.GetAddressBytes()[0] == 192 && ip.GetAddressBytes()[1] == 0 && ip.GetAddressBytes()[2] == 0 ||
            ip.GetAddressBytes()[0] == 198 && ip.GetAddressBytes()[1] is 18 or 19 ||
            ip.GetAddressBytes()[0] == 172 && ip.GetAddressBytes()[1] is >= 16 and <= 31);

    private static string? Secret(string? value, string blockId)
    {
        if (value is null || !value.StartsWith("${secret.", StringComparison.Ordinal) || !value.EndsWith('}')) return value;
        return Environment.GetEnvironmentVariable(value[9..^1]) ?? throw new PipelineDataException("Referenced server secret is unavailable.", blockId);
    }

    private static ColumnRecord MapColumn(string name, string dataType, DbColumn column, bool nullable)
    {
        var typeName = dataType.ToLowerInvariant();
        var type = typeName switch
        {
            "bool" or "boolean" => ColumnTypeRecord.Boolean,
            "int2" or "smallint" or "int4" or "integer" or "int8" or "bigint" or "smallserial" or "serial" or "bigserial" => ColumnTypeRecord.Int64,
            "numeric" or "decimal" or "money" when column.NumericPrecision is > 0 and <= 28 => ColumnTypeRecord.Decimal,
            "numeric" or "decimal" or "money" => ColumnTypeRecord.String,
            "float4" or "real" or "float8" or "double precision" => ColumnTypeRecord.Double,
            "date" => ColumnTypeRecord.Date,
            "timestamp" or "timestamp without time zone" or "timestamp with time zone" or "timestamptz" => ColumnTypeRecord.Timestamp,
            _ => ColumnTypeRecord.String
        };
        var precision = type == ColumnTypeRecord.Decimal ? Math.Clamp(column.NumericPrecision ?? 28, 1, 28) : 18;
        var scale = type == ColumnTypeRecord.Decimal ? Math.Clamp(column.NumericScale ?? 2, 0, precision) : 2;
        return new(name, type, nullable, precision, scale);
    }

    private static string? ToText(object? value, ColumnType type)
    {
        if (value is null or DBNull) return null;
        if (type == ColumnType.Date) return value switch
        {
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
        if (type == ColumnType.Timestamp) return value switch
        {
            DateTimeOffset timestamp => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            DateTime timestamp => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };
        return value switch
        {
            byte[] bytes => System.Convert.ToBase64String(bytes),
            Array array => JsonSerializer.Serialize(array),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }
}
