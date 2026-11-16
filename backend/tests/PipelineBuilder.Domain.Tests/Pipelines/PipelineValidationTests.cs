using Microsoft.VisualStudio.TestTools.UnitTesting;
using PipelineBuilder.Domain.Pipelines;
using Shouldly;

namespace PipelineBuilder.Domain.Tests.Pipelines;

[TestClass]
public sealed class PipelineValidationTests
{
    private PipelineDefinition _sut = null!;
    public PipelineValidationTests() => CreateSut();
    private void CreateSut() => _sut = new(1, [
        new CsvStep("csv", "customers.csv", [new("Age", ColumnType.String)]),
        new MapStep("map", [new("Age", new("age", ColumnType.Int64))]),
        new FilterStep("filter", new ComparisonPredicate("age", ComparisonOperator.GreaterOrEqual, "18")),
        new ParquetStep("parquet", "adults.parquet")
    ]);

    [TestMethod]
    public void Analyze_When_ColumnsAreMapped_Then_PropagatesSchema()
    {
        var result = PipelineValidation.Analyze(_sut);

        result.Issues.ShouldBeEmpty();
        result.Schemas[^1].Input.ShouldBe([new Column("age", ColumnType.Int64)]);
        result.Schemas[^1].Output.ShouldBeEmpty();
    }

    [TestMethod]
    public void Analyze_When_UpstreamColumnIsRenamed_Then_RejectsStaleFilter()
    {
        _sut = _sut with { Steps = _sut.Steps.Select(s => s is FilterStep f ? f with { Criteria = new ComparisonPredicate("Age", ComparisonOperator.Equal, "18") } : s).ToArray() };

        var result = PipelineValidation.Analyze(_sut);

        result.Issues.ShouldContain(i => i.BlockId == "filter" && i.Message.Contains("Unknown filter column"));
    }

    [TestMethod]
    public void Analyze_When_FilterConstantHasWrongType_Then_RejectsDefinition()
    {
        _sut = _sut with { Steps = [new CsvStep("csv", "in.csv", [new("age", ColumnType.Int64)]),
            new FilterStep("filter", new ComparisonPredicate("age", ComparisonOperator.Equal, "not-an-integer")), new ParquetStep("out", "out.parquet")] };

        var result = PipelineValidation.Analyze(_sut);

        result.Issues.ShouldContain(i => i.BlockId == "filter" && i.Message.Contains("constant"));
    }

    [TestMethod]
    public void Analyze_When_ApiInputUsesServerSecretReference_Then_AcceptsApiSource()
    {
        var pipeline = new PipelineDefinition(1, [
            new ApiInputStep("api", "https://example.com/items", [new("id", ColumnType.Int64)],
                Authentication: "bearer", Token: "${secret.API_TOKEN}", DataPath: "data.items"),
            new CsvOutputStep("out", "items.csv")
        ]);

        PipelineValidation.Analyze(pipeline).Issues.ShouldBeEmpty();
    }

    [TestMethod]
    public void Analyze_When_ApiInputContainsLiteralCredential_Then_RejectsIt()
    {
        var pipeline = new PipelineDefinition(1, [
            new ApiInputStep("api", "https://example.com/items", [new("id", ColumnType.Int64)],
                Authentication: "bearer", Token: "do-not-store"),
            new CsvOutputStep("out", "items.csv")
        ]);

        PipelineValidation.Analyze(pipeline).Issues.ShouldContain(issue => issue.BlockId == "api" && issue.Message.Contains("server-side"));
    }
}
