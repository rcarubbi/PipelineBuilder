using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Parquet;
using ExcelReader.Core.Reader;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Infrastructure.Files;
using Shouldly;

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
        _sut = new(new ServerPaths(options, new TestEnvironment { ContentRootPath = _root }));
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
