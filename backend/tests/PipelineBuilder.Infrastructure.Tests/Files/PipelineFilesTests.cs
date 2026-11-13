using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Parquet;
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
