using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using ExcelReader.Core.Reader;
using Parquet;
using Parquet.Schema;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Infrastructure.Files;

internal static class PipelineInputReader
{
    public static async IAsyncEnumerable<Dictionary<string, object?>> Read(PipelineStep input, string path, [EnumeratorCancellation] CancellationToken ct)
    {
        if (input is ApiInputStep api)
        {
            await foreach (var row in ApiInputReader.Read(api, ct)) yield return row;
            yield break;
        }
        if (input is CsvStep csvInput)
        {
            using var reader = new StreamReader(path, new UTF8Encoding(false, true));
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = csvInput.Delimiter, HasHeaderRecord = csvInput.HasHeader, MaxFieldSize = 1_048_576,
                ExceptionMessagesContainRawData = false, DetectColumnCountChanges = true
            });
            if (csvInput.HasHeader)
            {
                if (!await csv.ReadAsync()) throw new PipelineDataException("CSV header is missing.", input.Id);
                csv.ReadHeader();
                ValidateNames(csv.HeaderRecord!, csvInput.Schema, input.Id);
            }
            long rowNumber = 0;
            while (await csv.ReadAsync())
            {
                ct.ThrowIfCancellationRequested();
                rowNumber++;
                if (csv.Parser.Count != csvInput.Schema.Length) throw new PipelineDataException($"CSV record {rowNumber} has an unexpected number of fields.", input.Id);
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < csvInput.Schema.Length; i++)
                {
                    var column = csvInput.Schema[i];
                    row[column.Name] = PipelineFiles.ConvertValue(csvInput.HasHeader ? csv.GetField(column.Name) : csv.GetField(i), column, null, rowNumber, input.Id);
                }
                yield return row;
            }
        }
        else if (input is ExcelInputStep excel)
        {
            await using var workbook = await Excel.OpenAsync(path, ct: ct);
            if (!workbook.TryGetSheet(excel.Sheet, out var sheet)) throw new PipelineDataException("Excel sheet not found.", input.Id);
            using var reader = new SheetDataReader(sheet, new SheetDataReaderOptions { HeaderRow = excel.HasHeader ? 1 : 0 });
            var indexes = Enumerable.Range(0, excel.Schema.Length).ToArray();
            if (excel.HasHeader)
            {
                var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
                ValidateNames(names, excel.Schema, input.Id);
                indexes = excel.Schema.Select(column => Array.IndexOf(names, column.Name)).ToArray();
            }
            long rowNumber = 0;
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                rowNumber++;
                var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                for (var i = 0; i < excel.Schema.Length; i++)
                {
                    var column = excel.Schema[i];
                    var value = indexes[i] < reader.FieldCount ? reader.GetValue(indexes[i]) : null;
                    string? text = value is null or DBNull ? null : ValueConversion.Format(value);
                    if (value is DateTime date)
                    {
                        text = column.Type == ColumnType.Date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : DateTime.SpecifyKind(date, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
                    }
                    row[column.Name] = PipelineFiles.ConvertValue(text, column, null, rowNumber, input.Id);
                }
                yield return row;
            }
        }
        else if (input is ParquetInputStep parquetInput)
        {
            await using var reader = await ParquetReader.CreateAsync(path, cancellationToken: ct);
            var fields = reader.Schema.DataFields;
            ValidateNames(fields.Select(field => field.Name).ToArray(), parquetInput.Schema, input.Id);
            for (var groupIndex = 0; groupIndex < reader.RowGroupCount; groupIndex++)
            {
                using var group = reader.OpenRowGroupReader(groupIndex);
                var count = checked((int)group.RowCount);
                var columns = new Dictionary<string, object?[]>(StringComparer.Ordinal);
                foreach (var column in parquetInput.Schema)
                {
                    var field = fields.Single(f => f.Name == column.Name);
                    columns[column.Name] = await ReadColumn(group, field, column, count, ct);
                }
                for (var i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var row = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var column in parquetInput.Schema)
                    {
                        var value = columns[column.Name][i];
                        var text = ValueConversion.Format(value);
                        if (value is DateTime date && column.Type == ColumnType.Date)
                            text = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        else if (value is DateTime timestamp && column.Type == ColumnType.Timestamp)
                            text = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
                        row[column.Name] = PipelineFiles.ConvertValue(text, column, null, i + 1, input.Id);
                    }
                    yield return row;
                }
            }
        }
    }

    private static void ValidateNames(string[] names, Column[] schema, string id)
    {
        if (names.Length != schema.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
            !schema.All(column => names.Contains(column.Name, StringComparer.Ordinal)))
            throw new PipelineDataException("Input columns do not match the confirmed schema.", id);
    }

    private static async Task<object?[]> ReadColumn(ParquetRowGroupReader group, DataField field, Column column, int count, CancellationToken ct)
    {
        if (column.Type == ColumnType.String)
        {
            var values = new string?[count];
            await group.ReadAsync(field, values, cancellationToken: ct);
            return values;
        }
        return column.Type switch
        {
            ColumnType.Boolean => await ReadValues<bool>(group, field, count, ct),
            ColumnType.Int64 => await ReadValues<long>(group, field, count, ct),
            ColumnType.Decimal => await ReadValues<decimal>(group, field, count, ct),
            ColumnType.Double => await ReadValues<double>(group, field, count, ct),
            ColumnType.Date or ColumnType.Timestamp => await ReadValues<DateTime>(group, field, count, ct),
            _ => throw new PipelineDataException("Unsupported Parquet column type.")
        };
    }

    private static async Task<object?[]> ReadValues<T>(ParquetRowGroupReader group, DataField field, int count, CancellationToken ct) where T : struct
    {
        var values = new T?[count];
        await group.ReadAsync<T>(field, values, cancellationToken: ct);
        return values.Select(value => (object?)value).ToArray();
    }
}
