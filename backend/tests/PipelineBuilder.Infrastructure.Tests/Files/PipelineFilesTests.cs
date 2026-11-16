using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Parquet;
using ExcelReader.Core.Reader;
using Npgsql;
using Polly;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Domain.Pipelines;
using PipelineBuilder.Infrastructure.Files;
using Shouldly;
using Testcontainers.PostgreSql;

namespace PipelineBuilder.Infrastructure.Tests.Files;

[TestClass]
public sealed class PipelineFilesTests
{
    private PipelineFiles _sut = null!;
    private string _root = "";
    private readonly CancellationToken _ct = new CancellationTokenSource().Token;
    public PipelineFilesTests() => CreateSut();

    private void CreateSut()
    {
        _root = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "input"));
        Directory.CreateDirectory(Path.Combine(_root, "output"));
        var options = Options.Create(new PipelineFileOptions { InputRoot = "input", OutputRoot = "output" });
        _sut = new(new ServerPaths(options, new TestEnvironment { ContentRootPath = _root }), ResiliencePipeline.Empty);
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_root, true);

    private static DefinitionRecord Definition() => new(1, [
        new CsvRecord("csv", "data.csv", [new("age", ColumnTypeRecord.String), new("name", ColumnTypeRecord.String)]),
        new MapRecord("map", [new("age", new("age", ColumnTypeRecord.Int64)), new("name", new("full_name", ColumnTypeRecord.String))]),
        new FilterRecord("filter", new ComparisonRecord("age", "greaterOrEqual", "18")), new ParquetRecord("out", "adults.parquet")
    ]);

    [TestMethod]
    public async Task Execute_When_ValidCsv_Then_WritesFilteredTypedParquet()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "input/data.csv"), "age,name\n32,Ana\n17,Sam\n25,\"Lee, Jo\"\n", _ct);

        var result = await _sut.Execute(Definition(), _ => Task.FromResult(false), _ct);

        result.RowsRead.ShouldBe(3);
        result.RowsWritten.ShouldBe(2);
        await using var parquet = await ParquetReader.CreateAsync(Path.Combine(_root, "output/adults.parquet"), cancellationToken: _ct);
        using var group = parquet.OpenRowGroupReader(0);
        group.RowCount.ShouldBe(2);
        long[] ages = new long[2];
        await group.ReadAsync<long>(parquet.Schema.DataFields[0], ages, cancellationToken: _ct);
        ages.ShouldBe([32, 25]);
        string?[] names = new string?[2];
        await group.ReadAsync(parquet.Schema.DataFields[1], names, cancellationToken: _ct);
        names.ShouldBe(["Ana", "Lee, Jo"]);
    }

    [TestMethod]
    public async Task DiscoverParquet_When_FileHasPrimitiveFields_Then_UsesParquetMetadata()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "input/data.csv"), "age,name\n32,Ana\n", _ct);
        await _sut.Execute(Definition(), _ => Task.FromResult(false), _ct);
        File.Copy(Path.Combine(_root, "output/adults.parquet"), Path.Combine(_root, "input/adults.parquet"));

        var schema = await _sut.DiscoverParquet("adults.parquet", _ct);

        schema.Select(column => (column.Name, column.Type)).ShouldBe([
            ("age", ColumnTypeRecord.Int64), ("full_name", ColumnTypeRecord.String)
        ]);
    }

    [TestMethod]
    [TestCategory("Docker")]
    public async Task PostgresInput_When_QueryReturnsRows_Then_DiscoversAndStreamsTypedData()
    {
        await using var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await container.StartAsync(_ct);
        var connection = new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { SslMode = SslMode.Disable };
        var host = connection.Host!;
        var database = connection.Database!;
        var username = connection.Username!;
        var password = connection.Password!;
        var allowedHosts = Environment.GetEnvironmentVariable("PipelinePostgres__AllowedHosts");
        var testPassword = Environment.GetEnvironmentVariable("PG_TEST_PASSWORD");
        Environment.SetEnvironmentVariable("PipelinePostgres__AllowedHosts", host);
        Environment.SetEnvironmentVariable("PG_TEST_PASSWORD", password);
        try
        {
            await using (var db = new NpgsqlConnection(connection.ConnectionString))
            {
                await db.OpenAsync(_ct);
                await using (var create = new NpgsqlCommand("CREATE TABLE public.customers (id integer NOT NULL, name text NOT NULL)", db))
                    await create.ExecuteNonQueryAsync(_ct);
                await using var insert = new NpgsqlCommand("INSERT INTO public.customers VALUES (7, 'Ana')", db);
                await insert.ExecuteNonQueryAsync(_ct);
            }
            var input = new PostgresInputStep("postgres", host, connection.Port.ToString(), database,
                username, "${secret.PG_TEST_PASSWORD}", "Disable", "SELECT id, name FROM public.customers", []);

            var discovered = await _sut.DiscoverPostgres(input, _ct);

            discovered.Select(column => (column.Name, column.Type, column.Nullable)).ShouldBe([
                ("id", ColumnTypeRecord.Int64, false), ("name", ColumnTypeRecord.String, false)
            ]);
            var definition = new DefinitionRecord(1, [
                new PostgresInputRecord(input.Id, input.Host, input.Port, input.Database, input.Username, input.Password, input.SslMode, input.Query, discovered),
                new CsvOutputRecord("out", "postgres.csv")
            ]);
            var result = await _sut.Execute(definition, _ => Task.FromResult(false), _ct);

            result.RowsRead.ShouldBe(1);
            result.RowsWritten.ShouldBe(1);
            (await File.ReadAllTextAsync(Path.Combine(_root, "output/postgres.csv"), _ct)).Replace("\r\n", "\n").ShouldBe("id,name\n7,Ana\n");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PipelinePostgres__AllowedHosts", allowedHosts);
            Environment.SetEnvironmentVariable("PG_TEST_PASSWORD", testPassword);
        }
    }

    [TestMethod]
    public async Task DiscoverExcel_When_HeaderAndRowsExist_Then_InfersColumnTypes()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "input/data.csv"), "age,name\n32,Ana\n25,Bia\n", _ct);
        var export = Definition() with { Steps = [.. Definition().Steps[..^1], new ExcelOutputRecord("excel", "sample.xlsx", "Data")] };
        await _sut.Execute(export, _ => Task.FromResult(false), _ct);
        File.Copy(Path.Combine(_root, "output/sample.xlsx"), Path.Combine(_root, "input/sample.xlsx"));

        var schema = await _sut.DiscoverExcel("sample.xlsx", "Data", true, _ct);

        schema.Select(column => (column.Name, column.Type)).ShouldBe([
            ("age", ColumnTypeRecord.Int64), ("full_name", ColumnTypeRecord.String)
        ]);
    }

    [TestMethod]
    public async Task Execute_When_CsvOutputSelected_Then_WritesFilteredRowsWithEscaping()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "input/data.csv"), "age,name\n32,\"Lee, Jo\"\n17,Sam\n", _ct);
        var definition = Definition() with { Steps = [.. Definition().Steps[..^1], new CsvOutputRecord("out", "adults.csv")] };

        var result = await _sut.Execute(definition, _ => Task.FromResult(false), _ct);

        result.RowsRead.ShouldBe(2);
        result.RowsWritten.ShouldBe(1);
        var content = await File.ReadAllTextAsync(Path.Combine(_root, "output/adults.csv"), _ct);
        content.Replace("\r\n", "\n").ShouldBe("age,full_name\n32,\"Lee, Jo\"\n");
    }

    [TestMethod]
    [DataRow(".xls")]
    [DataRow(".xlsx")]
    public async Task Execute_When_ExcelOutputAndInputUseNamedSheet_Then_RoundTripsRows(string extension)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "input/data.csv"), "age,name\n32,Ana\n17,Sam\n", _ct);
        var export = Definition() with { Steps = [.. Definition().Steps[..^1], new ExcelOutputRecord("excel", "adults" + extension, "Results")] };
        var result = await _sut.Execute(export, _ => Task.FromResult(false), _ct);
        result.RowsWritten.ShouldBe(1);
        var exported = Path.Combine(_root, "output", "adults" + extension);
        await using (var book = await Excel.OpenAsync(exported, ct: _ct))
        {
            book.TryGetSheet("Results", out var sheet).ShouldBeTrue();
            using var reader = new SheetDataReader(sheet);
            reader.GetName(0).ShouldBe("age");
            reader.Read().ShouldBeTrue();
            System.Convert.ToDouble(reader.GetValue(0)).ShouldBe(32d);
        }
        File.Copy(exported, Path.Combine(_root, "input", "adults" + extension));
        var import = new DefinitionRecord(1, [
            new ExcelInputRecord("input", "adults" + extension, [new("age", ColumnTypeRecord.Int64), new("full_name", ColumnTypeRecord.String)], "Results"),
            new CsvOutputRecord("output", "roundtrip" + extension + ".csv")
        ]);
        var imported = await _sut.Execute(import, _ => Task.FromResult(false), _ct);
        imported.RowsWritten.ShouldBe(1);
        (await File.ReadAllTextAsync(Path.Combine(_root, "output", "roundtrip" + extension + ".csv"), _ct)).Replace("\r\n", "\n").ShouldBe("age,full_name\n32,Ana\n");
    }

    [TestMethod]
    public async Task Execute_When_ParquetInputSelected_Then_ReadsTypedRows()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "input/data.csv"), "age,name\n32,Ana\n17,Sam\n", _ct);
        await _sut.Execute(Definition(), _ => Task.FromResult(false), _ct);
        File.Copy(Path.Combine(_root, "output/adults.parquet"), Path.Combine(_root, "input/adults.parquet"));
        var import = new DefinitionRecord(1, [
            new ParquetInputRecord("input", "adults.parquet", [new("age", ColumnTypeRecord.Int64), new("full_name", ColumnTypeRecord.String)]),
            new CsvOutputRecord("output", "from-parquet.csv")
        ]);
        var result = await _sut.Execute(import, _ => Task.FromResult(false), _ct);
        result.RowsRead.ShouldBe(1);
        (await File.ReadAllTextAsync(Path.Combine(_root, "output/from-parquet.csv"), _ct)).Replace("\r\n", "\n").ShouldBe("age,full_name\n32,Ana\n");
    }

    [TestMethod]
    public async Task Execute_When_ExcelSheetIsMissing_Then_DoesNotPublishOutput()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "input/data.csv"), "age,name\n32,Ana\n", _ct);
        var export = Definition() with { Steps = [.. Definition().Steps[..^1], new ExcelOutputRecord("excel", "source.xlsx", "Actual")] };
        await _sut.Execute(export, _ => Task.FromResult(false), _ct);
        File.Copy(Path.Combine(_root, "output/source.xlsx"), Path.Combine(_root, "input/source.xlsx"));
        var import = new DefinitionRecord(1, [
            new ExcelInputRecord("input", "source.xlsx", [new("age", ColumnTypeRecord.Int64), new("full_name", ColumnTypeRecord.String)], "Missing"),
            new CsvOutputRecord("output", "should-not-exist.csv")
        ]);
        var error = await Should.ThrowAsync<PipelineDataException>(() => _sut.Execute(import, _ => Task.FromResult(false), _ct));
        error.BlockId.ShouldBe("input");
        File.Exists(Path.Combine(_root, "output/should-not-exist.csv")).ShouldBeFalse();
        Directory.GetFiles(Path.Combine(_root, "output"), "*.tmp").ShouldBeEmpty();
    }

    [TestMethod]
    [DataRow(".xls")]
    [DataRow(".xlsx")]
    public async Task Execute_When_ExcelHasNoHeader_Then_FirstRowIsPreserved(string extension)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "input/data.csv"), "age,name\n32,Ana\n25,Bia\n", _ct);
        var export = Definition() with { Steps = [.. Definition().Steps[..^1], new ExcelOutputRecord("excel", "noheader" + extension, "Data", HasHeader: false)] };
        await _sut.Execute(export, _ => Task.FromResult(false), _ct);
        File.Copy(Path.Combine(_root, "output", "noheader" + extension), Path.Combine(_root, "input", "noheader" + extension));
        var import = new DefinitionRecord(1, [
            new ExcelInputRecord("input", "noheader" + extension, [new("age", ColumnTypeRecord.Int64), new("full_name", ColumnTypeRecord.String)], "Data", HasHeader: false),
            new CsvOutputRecord("output", "noheader.csv")
        ]);

        var result = await _sut.Execute(import, _ => Task.FromResult(false), _ct);

        result.RowsRead.ShouldBe(2);
        (await File.ReadAllTextAsync(Path.Combine(_root, "output/noheader.csv"), _ct)).Replace("\r\n", "\n").ShouldBe("age,full_name\n32,Ana\n25,Bia\n");
    }

    [TestMethod]
    [DataRow("application/json", "{\"payload\":{\"items\":[{\"id\":7},{\"id\":8}]}}", "payload.items", "json")]
    [DataRow("application/xml", "<root><items><item><id>7</id></item><item><id>8</id></item></items></root>", "root/items", "xml")]
    public async Task Execute_When_ApiReturnsNestedJsonOrXml_Then_ExtractsRows(string contentType, string payload, string dataPath, string format)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var previousAllowedHosts = Environment.GetEnvironmentVariable("PipelineApi__AllowedHosts");
        Environment.SetEnvironmentVariable("PipelineApi__AllowedHosts", "127.0.0.1");
        var server = Task.Run(async () =>
        {
            for (var requestNumber = 0; requestNumber < 2; requestNumber++)
            {
                using var client = await listener.AcceptTcpClientAsync(_ct);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                string? line;
                do { line = await reader.ReadLineAsync(_ct); } while (!string.IsNullOrEmpty(line));
                var bytes = Encoding.UTF8.GetBytes(payload);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {contentType}; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, _ct);
                await stream.WriteAsync(bytes, _ct);
            }
        }, _ct);
        try
        {
            var api = new ApiInputStep("api", $"http://127.0.0.1:{port}/items", [], dataPath, Format: format);
            var discovery = await _sut.DiscoverApi(api, _ct);
            discovery.Schema.Select(column => column.Name).ShouldBe(["id"]);
            discovery.DataPaths.ShouldContain(format == "json" ? "payload.items" : "items");
            var schema = discovery.Schema;
            var definition = new DefinitionRecord(1, [
                new ApiInputRecord("api", $"http://127.0.0.1:{port}/items", schema, dataPath, Format: format),
                new CsvOutputRecord("out", "api.csv")
            ]);

            var result = await _sut.Execute(definition, _ => Task.FromResult(false), _ct);

            result.RowsRead.ShouldBe(2);
            result.RowsWritten.ShouldBe(2);
            (await File.ReadAllTextAsync(Path.Combine(_root, "output/api.csv"), _ct)).Replace("\r\n", "\n").ShouldBe("id\n7\n8\n");
            await server;
        }
        finally
        {
            Environment.SetEnvironmentVariable("PipelineApi__AllowedHosts", previousAllowedHosts);
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_When_ApiUsesLinkHeaderPagination_Then_FollowsRelativeNextLink()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var previousAllowedHosts = Environment.GetEnvironmentVariable("PipelineApi__AllowedHosts");
        Environment.SetEnvironmentVariable("PipelineApi__AllowedHosts", "127.0.0.1");
        var server = Task.Run(async () =>
        {
            for (var page = 1; page <= 2; page++)
            {
                using var client = await listener.AcceptTcpClientAsync(_ct);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync(_ct);
                string? line;
                do { line = await reader.ReadLineAsync(_ct); } while (!string.IsNullOrEmpty(line));
                requestLine!.ShouldContain(page == 1 ? "GET /items " : "GET /items?page=2 ");
                var body = page == 1 ? "{\"items\":[{\"id\":1}]}" : "{\"items\":[{\"id\":2}]}";
                var bytes = Encoding.UTF8.GetBytes(body);
                var link = page == 1 ? $"Link: </items?page=2>; rel=\"next\"\r\n" : "";
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\n{link}Connection: close\r\n\r\n");
                await stream.WriteAsync(headers, _ct);
                await stream.WriteAsync(bytes, _ct);
            }
        }, _ct);
        try
        {
            var definition = new DefinitionRecord(1, [
                new ApiInputRecord("api", $"http://127.0.0.1:{port}/items", [new("id", ColumnTypeRecord.Int64)], "items", UseLinkHeader: true),
                new CsvOutputRecord("out", "pages.csv")
            ]);

            var result = await _sut.Execute(definition, _ => Task.FromResult(false), _ct);

            result.RowsRead.ShouldBe(2);
            (await File.ReadAllTextAsync(Path.Combine(_root, "output/pages.csv"), _ct)).Replace("\r\n", "\n").ShouldBe("id\n1\n2\n");
            await server;
        }
        finally
        {
            Environment.SetEnvironmentVariable("PipelineApi__AllowedHosts", previousAllowedHosts);
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_When_ApiContainsNestedArraysAndObjects_Then_FlattensRowsAndDiscoversFieldPaths()
    {
        const string payload = "{\"data\":{\"rows\":[[{\"customer\":{\"id\":7}},{\"customer\":{\"id\":8}}]]}}";
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var previousAllowedHosts = Environment.GetEnvironmentVariable("PipelineApi__AllowedHosts");
        Environment.SetEnvironmentVariable("PipelineApi__AllowedHosts", "127.0.0.1");
        var server = Task.Run(async () =>
        {
            for (var requestNumber = 0; requestNumber < 2; requestNumber++)
            {
                using var client = await listener.AcceptTcpClientAsync(_ct);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                string? line;
                do { line = await reader.ReadLineAsync(_ct); } while (!string.IsNullOrEmpty(line));
                var bytes = Encoding.UTF8.GetBytes(payload);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, _ct);
                await stream.WriteAsync(bytes, _ct);
            }
        }, _ct);
        try
        {
            var url = $"http://127.0.0.1:{port}/items";
            var api = new ApiInputStep("api", url, [], "data.rows");
            var discovery = await _sut.DiscoverApi(api, _ct);
            discovery.Schema.Select(column => column.Name).ShouldBe(["customer.id"]);
            discovery.DataPaths.ShouldContain("data.rows");

            var result = await _sut.Execute(new DefinitionRecord(1, [
                new ApiInputRecord("api", url, discovery.Schema, "data.rows"),
                new CsvOutputRecord("out", "nested.csv")
            ]), _ => Task.FromResult(false), _ct);

            result.RowsRead.ShouldBe(2);
            (await File.ReadAllTextAsync(Path.Combine(_root, "output/nested.csv"), _ct))
                .Replace("\r\n", "\n").ShouldBe("customer.id\n7\n8\n");
            await server;
        }
        finally
        {
            Environment.SetEnvironmentVariable("PipelineApi__AllowedHosts", previousAllowedHosts);
            listener.Stop();
        }
    }

    [TestMethod]
    public async Task Execute_When_RowCannotBeConverted_Then_PublishesNothingAndDeletesTemporaryFile()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "input/data.csv"), "age,name\nsecret-invalid-value,Ana\n", _ct);

        var error = await Should.ThrowAsync<PipelineDataException>(() => _sut.Execute(Definition(), _ => Task.FromResult(false), _ct));

        error.BlockId.ShouldBe("map");
        error.Message.ShouldNotContain("secret-invalid-value");
        Directory.GetFiles(Path.Combine(_root, "output")).ShouldBeEmpty();
    }

    [TestMethod]
    public async Task Execute_When_OutputExists_Then_DoesNotOverwrite()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "input/data.csv"), "age,name\n32,Ana\n", _ct);
        var output = Path.Combine(_root, "output/adults.parquet");
        await File.WriteAllTextAsync(output, "keep me", _ct);

        var error = await Should.ThrowAsync<PipelineDataException>(() => _sut.Execute(Definition(), _ => Task.FromResult(false), _ct));

        error.Message.ShouldBe("Output already exists; choose a different filename.");
        error.BlockId.ShouldBe("out");
        (await File.ReadAllTextAsync(output, _ct)).ShouldBe("keep me");
    }

    [TestMethod]
    public async Task Execute_When_ConcurrentRunsShareOutput_Then_OnlyOnePublishes()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "input/data.csv"), "age,name\n32,Ana\n", _ct);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        async Task<bool> WaitForBoth(CancellationToken ct)
        {
            if (Interlocked.Increment(ref count) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(ct);
            return false;
        }
        async Task<bool> Run()
        {
            try { await _sut.Execute(Definition(), WaitForBoth, timeout.Token); return true; }
            catch (PipelineDataException) { return false; }
        }

        var results = await Task.WhenAll(Run(), Run()).WaitAsync(timeout.Token);

        results.Count(success => success).ShouldBe(1);
        Directory.GetFiles(Path.Combine(_root, "output")).Length.ShouldBe(1);
        await using var parquet = await ParquetReader.CreateAsync(Path.Combine(_root, "output/adults.parquet"), cancellationToken: _ct);
        using var group = parquet.OpenRowGroupReader(0);
        group.RowCount.ShouldBe(1);
    }

    [TestMethod]
    public async Task Execute_When_Cancelled_Then_PublishesNothing()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "input/data.csv"), "age,name\n32,Ana\n", _ct);

        await Should.ThrowAsync<OperationCanceledException>(() => _sut.Execute(Definition(), _ => Task.FromResult(true), _ct));

        Directory.GetFiles(Path.Combine(_root, "output")).ShouldBeEmpty();
    }

    [TestMethod]
    public async Task Discover_When_SampleHasNullableAndQuotedFields_Then_InfersConservativeSchema()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "input/data.csv"), "age,name\n32,\"Lee, Jo\"\n,Ana\n", _ct);

        var schema = await _sut.Discover("data.csv", ",", true, _ct);

        schema[0].Type.ShouldBe(ColumnTypeRecord.Int64);
        schema[0].Nullable.ShouldBeTrue();
        schema[1].Type.ShouldBe(ColumnTypeRecord.String);
    }

    [TestMethod]
    public async Task Discover_When_PathEscapesRoot_Then_RejectsAccess() =>
        await Should.ThrowAsync<PipelineDataException>(() => _sut.Discover("../outside.csv", ",", true, _ct));

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "PipelineBuilder";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
