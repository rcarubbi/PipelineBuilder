using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Runs;
using Shouldly;

namespace PipelineBuilder.Application.Tests.UseCases.Runs;

[TestClass]
public sealed class StartRunCommandTests
{
    private readonly Mock<IDesignerQueries> _queries = new();
    private readonly Mock<IPipelineRepository> _repository = new();
    private StartRunCommand _sut = null!;
    private SavedPipelineRecord _pipeline = null!;
    private readonly CancellationToken _ct = new CancellationTokenSource().Token;
    public StartRunCommandTests() => CreateSut();

    private void CreateSut()
    {
        _queries.Reset(); _repository.Reset();
        _pipeline = new(Guid.NewGuid(), 2, new("Example", new(1, [new CsvRecord("csv", "${INPUT}", [new("age", ColumnTypeRecord.Int64)]), new ParquetRecord("out", "${OUTPUT}")]),
            JsonSerializer.SerializeToElement(new { }), new() { ["test"] = new() { ["INPUT"] = "test.csv", ["OUTPUT"] = "test.parquet" } }));
        _queries.Setup(q => q.GetPipeline(_pipeline.Id, _ct)).ReturnsAsync(_pipeline);
        _repository.Setup(r => r.Enqueue(_pipeline.Id, 2, It.IsAny<RunSnapshotRecord>(), _ct)).ReturnsAsync(new RunRecord(Guid.NewGuid(), _pipeline.Id, 2, "test", "Queued", DateTimeOffset.UtcNow, null, null, 0, 0, null, null));
        _sut = new(_queries.Object, _repository.Object);
    }

    [TestMethod]
    public async Task Handle_When_EnvironmentSelected_Then_QueuesResolvedSnapshotWithoutChangingDefinition()
    {
        var result = await _sut.Handle(new(_pipeline.Id, 2, "test"), _ct);

        result.IsError.ShouldBeFalse();
        _repository.Verify(r => r.Enqueue(_pipeline.Id, 2, It.Is<RunSnapshotRecord>(s => ((CsvRecord)s.Definition.Steps[0]).Path == "test.csv" && ((ParquetRecord)s.Definition.Steps[1]).Path == "test.parquet"), _ct), Times.Once);
        ((CsvRecord)_pipeline.Document.Definition.Steps[0]).Path.ShouldBe("${INPUT}");
    }

    [TestMethod]
    public async Task Handle_When_VariableMissing_Then_DoesNotQueueRun()
    {
        _pipeline.Document.Environments["test"].Remove("INPUT");

        var result = await _sut.Handle(new(_pipeline.Id, 2, "test"), _ct);

        result.IsError.ShouldBeTrue();
        _repository.Verify(r => r.Enqueue(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<RunSnapshotRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_When_RevisionIsStale_Then_RejectsRun()
    {
        var result = await _sut.Handle(new(_pipeline.Id, 1, "test"), _ct);

        result.FirstError.Type.ShouldBe(ErrorOr.ErrorType.Conflict);
        _repository.Verify(r => r.Enqueue(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<RunSnapshotRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_When_PathUsesExpressions_Then_ResolvesEnvironmentVariableAndUtcInSnapshot()
    {
        var input = new TextEnvironmentRecord("env", "INPUT");
        var output = new TextConcatRecord("concat", new TextVariableRecord("ref", "prefix"), new TextUtcRecord("clock", "yyyyMMdd"));
        var definition = new DefinitionRecord(1,
            [new CsvRecord("csv", "expression", [new("age", ColumnTypeRecord.Int64)], PathExpression: input),
             new ParquetRecord("out", "expression", output)],
            [new VariableRecord("prefix", new TextLiteralRecord("literal", "export-"))]);
        _pipeline = _pipeline with { Document = _pipeline.Document with { Definition = definition } };
        _queries.Setup(q => q.GetPipeline(_pipeline.Id, _ct)).ReturnsAsync(_pipeline);

        var result = await _sut.Handle(new(_pipeline.Id, 2, "test"), _ct);

        result.IsError.ShouldBeFalse();
        _repository.Verify(r => r.Enqueue(_pipeline.Id, 2, It.Is<RunSnapshotRecord>(s =>
            ((CsvRecord)s.Definition.Steps[0]).Path == "test.csv" &&
            ((ParquetRecord)s.Definition.Steps[1]).Path.StartsWith("export-") &&
            ((ParquetRecord)s.Definition.Steps[1]).Path.Length == 15 &&
            ((ParquetRecord)s.Definition.Steps[1]).PathExpression == null), _ct), Times.Once);
    }

    [TestMethod]
    public async Task Handle_When_ApiTextPropertiesUseVariableScopes_Then_ResolvesAllAndKeepsSecretReference()
    {
        var definition = new DefinitionRecord(1, [
            new ApiInputRecord("api", "https://${env.HOST}/v1", [new("${var.COLUMN}", ColumnTypeRecord.String, true,
                TypeVariable: "${env.COLUMN_TYPE}", NullableVariable: "${env.COLUMN_NULLABLE}")],
                DataPath: "${var.DATA_PATH}", Method: "${var.API_METHOD}", Authentication: "bearer", Token: "${secret.API_TOKEN}",
                Headers: new() { ["X-Tenant"] = "${env.TENANT}" }, UseLinkHeaderVariable: "${env.LINK_HEADER}"),
            new MapRecord("map", [new MappingRecord("id", new ColumnRecord("amount", ColumnTypeRecord.Decimal,
                PrecisionVariable: "${env.PRECISION}", ScaleVariable: "${env.SCALE}"))]),
            new FilterRecord("filter", new LogicalRecord("and", [
                new ComparisonRecord("amount", "equal", "7", OperatorVariable: "${env.OPERATOR}"),
                new NullRecord("amount", true, "${env.IS_NULL}")
            ])),
            new CsvOutputRecord("out", "result.csv", "${env.DELIMITER}", HasHeaderVariable: "${env.HAS_HEADER}")
        ], [new VariableRecord("COLUMN", new TextLiteralRecord("column", "id")),
            new VariableRecord("DATA_PATH", new TextLiteralRecord("path", "payload.items")),
            new VariableRecord("API_METHOD", new TextLiteralRecord("method", "POST"))]);
        _pipeline = _pipeline with
        {
            Document = _pipeline.Document with
            {
                Definition = definition,
                Environments = new() { ["test"] = new() { ["HOST"] = "api.example.com", ["TENANT"] = "tenant-1", ["DELIMITER"] = ";", ["LINK_HEADER"] = "true", ["HAS_HEADER"] = "false", ["COLUMN_TYPE"] = "int64", ["COLUMN_NULLABLE"] = "false", ["OPERATOR"] = "greater", ["IS_NULL"] = "false", ["PRECISION"] = "12", ["SCALE"] = "3" } }
            }
        };
        _queries.Setup(q => q.GetPipeline(_pipeline.Id, _ct)).ReturnsAsync(_pipeline);

        var result = await _sut.Handle(new(_pipeline.Id, 2, "test"), _ct);

        result.IsError.ShouldBeFalse();
        _repository.Verify(r => r.Enqueue(_pipeline.Id, 2, It.Is<RunSnapshotRecord>(snapshot =>
            ((ApiInputRecord)snapshot.Definition.Steps[0]).Url == "https://api.example.com/v1" &&
            ((ApiInputRecord)snapshot.Definition.Steps[0]).Method == "POST" &&
            ((ApiInputRecord)snapshot.Definition.Steps[0]).Schema[0].Name == "id" &&
            ((ApiInputRecord)snapshot.Definition.Steps[0]).Schema[0].Type == ColumnTypeRecord.Int64 &&
            !((ApiInputRecord)snapshot.Definition.Steps[0]).Schema[0].Nullable &&
            ((ApiInputRecord)snapshot.Definition.Steps[0]).DataPath == "payload.items" &&
            ((ApiInputRecord)snapshot.Definition.Steps[0]).Token == "${secret.API_TOKEN}" &&
            ((ApiInputRecord)snapshot.Definition.Steps[0]).Headers!["X-Tenant"] == "tenant-1" &&
            ((ApiInputRecord)snapshot.Definition.Steps[0]).UseLinkHeader &&
            ((ApiInputRecord)snapshot.Definition.Steps[0]).UseLinkHeaderVariable == null &&
            ((MapRecord)snapshot.Definition.Steps[1]).Mappings[0].Target.Precision == 12 &&
            ((MapRecord)snapshot.Definition.Steps[1]).Mappings[0].Target.Scale == 3 &&
            ((MapRecord)snapshot.Definition.Steps[1]).Mappings[0].Target.PrecisionVariable == null &&
            ((ComparisonRecord)((LogicalRecord)((FilterRecord)snapshot.Definition.Steps[2]).Criteria).Children[0]).Operator == "greater" &&
            ((ComparisonRecord)((LogicalRecord)((FilterRecord)snapshot.Definition.Steps[2]).Criteria).Children[0]).OperatorVariable == null &&
            !((NullRecord)((LogicalRecord)((FilterRecord)snapshot.Definition.Steps[2]).Criteria).Children[1]).IsNull &&
            ((CsvOutputRecord)snapshot.Definition.Steps[3]).Delimiter == ";" &&
            !((CsvOutputRecord)snapshot.Definition.Steps[3]).HasHeader &&
            ((CsvOutputRecord)snapshot.Definition.Steps[3]).HasHeaderVariable == null), _ct), Times.Once);
    }

    [TestMethod]
    public async Task Handle_When_BooleanPropertyVariableIsNotBoolean_Then_RejectsRun()
    {
        var definition = new DefinitionRecord(1, [
            new CsvRecord("csv", "input.csv", [new("id", ColumnTypeRecord.Int64)]),
            new CsvOutputRecord("out", "output.csv", HasHeaderVariable: "${env.HAS_HEADER}")
        ]);
        _pipeline = _pipeline with
        {
            Document = _pipeline.Document with
            {
                Definition = definition,
                Environments = new() { ["test"] = new() { ["HAS_HEADER"] = "sometimes" } }
            }
        };
        _queries.Setup(q => q.GetPipeline(_pipeline.Id, _ct)).ReturnsAsync(_pipeline);

        var result = await _sut.Handle(new(_pipeline.Id, 2, "test"), _ct);

        result.IsError.ShouldBeTrue();
        result.FirstError.Description.ShouldContain("true");
        _repository.Verify(r => r.Enqueue(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<RunSnapshotRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_When_ColumnTypeVariableResolvesToUndefinedNumericEnum_Then_RejectsRun()
    {
        var definition = new DefinitionRecord(1, [
            new CsvRecord("csv", "input.csv", [new("id", ColumnTypeRecord.String, TypeVariable: "${env.COLUMN_TYPE}")]),
            new CsvOutputRecord("out", "output.csv")
        ]);
        _pipeline = _pipeline with
        {
            Document = _pipeline.Document with
            {
                Definition = definition,
                Environments = new() { ["test"] = new() { ["COLUMN_TYPE"] = "999" } }
            }
        };
        _queries.Setup(q => q.GetPipeline(_pipeline.Id, _ct)).ReturnsAsync(_pipeline);

        var result = await _sut.Handle(new(_pipeline.Id, 2, "test"), _ct);

        result.IsError.ShouldBeTrue();
        result.FirstError.Description.ShouldContain("supported type");
        _repository.Verify(r => r.Enqueue(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<RunSnapshotRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_When_ComparisonOperatorVariableIsUnsupported_Then_RejectsRun()
    {
        var definition = new DefinitionRecord(1, [
            new CsvRecord("csv", "input.csv", [new("id", ColumnTypeRecord.Int64)]),
            new FilterRecord("filter", new ComparisonRecord("id", "equal", "1", OperatorVariable: "${env.OPERATOR}")),
            new CsvOutputRecord("out", "output.csv")
        ]);
        _pipeline = _pipeline with
        {
            Document = _pipeline.Document with
            {
                Definition = definition,
                Environments = new() { ["test"] = new() { ["OPERATOR"] = "999" } }
            }
        };
        _queries.Setup(q => q.GetPipeline(_pipeline.Id, _ct)).ReturnsAsync(_pipeline);

        var result = await _sut.Handle(new(_pipeline.Id, 2, "test"), _ct);

        result.IsError.ShouldBeTrue();
        result.FirstError.Description.ShouldContain("supported operator");
        _repository.Verify(r => r.Enqueue(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<RunSnapshotRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_When_CurrentEnvironmentIsReferenced_Then_UsesSelectedNameForPathsAndExpressions()
    {
        _pipeline.Document.Environments["test"]["CURRENT_ENVIRONMENT"] = "other";
        var definition = new DefinitionRecord(1,
            [new CsvRecord("csv", "${CURRENT_ENVIRONMENT}.csv", [new("age", ColumnTypeRecord.Int64)]),
             new ParquetRecord("out", "expression", new TextConcatRecord("concat",
                 new TextEnvironmentRecord("env", "CURRENT_ENVIRONMENT"), new TextLiteralRecord("suffix", ".parquet")))]);
        _pipeline = _pipeline with { Document = _pipeline.Document with { Definition = definition } };
        _queries.Setup(q => q.GetPipeline(_pipeline.Id, _ct)).ReturnsAsync(_pipeline);

        var result = await _sut.Handle(new(_pipeline.Id, 2, "test"), _ct);

        result.IsError.ShouldBeFalse();
        _repository.Verify(r => r.Enqueue(_pipeline.Id, 2, It.Is<RunSnapshotRecord>(s =>
            ((CsvRecord)s.Definition.Steps[0]).Path == "test.csv" &&
            ((ParquetRecord)s.Definition.Steps[1]).Path == "test.parquet"), _ct), Times.Once);
    }

    [TestMethod]
    public async Task Handle_When_ComparisonAndUtcFormatUseVariables_Then_ResolvesBeforeValidation()
    {
        var definition = new DefinitionRecord(1,
            [new CsvRecord("csv", "input.csv", [new("age", ColumnTypeRecord.Int64)]),
             new FilterRecord("filter", new ComparisonRecord("missing", "greater", "invalid",
                 new TextVariableRecord("column-ref", "column"), new TextVariableRecord("value-ref", "threshold"))),
             new ParquetRecord("out", "expression", new TextUtcRecord("utc", "invalid", new TextVariableRecord("format-ref", "format")))],
            [new VariableRecord("column", new TextLiteralRecord("column-value", "age")),
             new VariableRecord("threshold", new TextLiteralRecord("threshold-value", "18")),
             new VariableRecord("format", new TextLiteralRecord("format-value", "'fixed.parquet'"))]);
        _pipeline = _pipeline with { Document = _pipeline.Document with { Definition = definition } };
        _queries.Setup(q => q.GetPipeline(_pipeline.Id, _ct)).ReturnsAsync(_pipeline);

        var result = await _sut.Handle(new(_pipeline.Id, 2, "test"), _ct);

        result.IsError.ShouldBeFalse();
        _repository.Verify(r => r.Enqueue(_pipeline.Id, 2, It.Is<RunSnapshotRecord>(s =>
            ((ComparisonRecord)((FilterRecord)s.Definition.Steps[1]).Criteria).Column == "age" &&
            ((ComparisonRecord)((FilterRecord)s.Definition.Steps[1]).Criteria).Value == "18" &&
            ((ComparisonRecord)((FilterRecord)s.Definition.Steps[1]).Criteria).ColumnExpression == null &&
            ((ParquetRecord)s.Definition.Steps[2]).Path == "fixed.parquet"), _ct), Times.Once);
    }

    [TestMethod]
    public async Task Handle_When_OutputVariableCombinesEnvironmentAndUtc_Then_ResolvesSnapshot()
    {
        var output = new TextConcatRecord("outer",
            new TextConcatRecord("prefix", new TextLiteralRecord("name", "customers_"),
                new TextEnvironmentRecord("env", "CURRENT_ENVIRONMENT")),
            new TextConcatRecord("suffix", new TextLiteralRecord("separator", "_"),
                new TextConcatRecord("timestamp", new TextUtcRecord("utc", "yyyyMMdd_HHmmss"),
                    new TextLiteralRecord("extension", ".parquet"))));
        var definition = new DefinitionRecord(1,
            [new CsvRecord("csv", "expression", [new("age", ColumnTypeRecord.Int64)], PathExpression: new TextEnvironmentRecord("input", "name")),
             new ParquetRecord("out", "expression", new TextVariableRecord("output-ref", "outputname"))],
            [new VariableRecord("outputname", output)]);
        _pipeline.Document.Environments["test"]["name"] = "customers.csv";
        _pipeline = _pipeline with { Document = _pipeline.Document with { Definition = definition } };
        _queries.Setup(q => q.GetPipeline(_pipeline.Id, _ct)).ReturnsAsync(_pipeline);

        var result = await _sut.Handle(new(_pipeline.Id, 2, "test"), _ct);

        result.IsError.ShouldBeFalse();
        _repository.Verify(r => r.Enqueue(_pipeline.Id, 2, It.Is<RunSnapshotRecord>(s =>
            ((CsvRecord)s.Definition.Steps[0]).Path == "customers.csv" &&
            ((ParquetRecord)s.Definition.Steps[1]).Path.StartsWith("customers_test_") &&
            ((ParquetRecord)s.Definition.Steps[1]).Path.EndsWith(".parquet")), _ct), Times.Once);
    }

    [TestMethod]
    public async Task Handle_When_ExcelPathsUseEnvironment_Then_ResolvesInputAndOutput()
    {
        var definition = new DefinitionRecord(1,
            [new ExcelInputRecord("input", "${INPUT}", [new("age", ColumnTypeRecord.Int64)], "Source"),
             new ExcelOutputRecord("out", "expression", "Results", PathExpression: new TextEnvironmentRecord("env", "OUTPUT"))]);
        _pipeline = _pipeline with { Document = _pipeline.Document with { Definition = definition } };
        _queries.Setup(q => q.GetPipeline(_pipeline.Id, _ct)).ReturnsAsync(_pipeline);

        var result = await _sut.Handle(new(_pipeline.Id, 2, "test"), _ct);

        result.IsError.ShouldBeFalse();
        _repository.Verify(r => r.Enqueue(_pipeline.Id, 2, It.Is<RunSnapshotRecord>(s =>
            ((ExcelInputRecord)s.Definition.Steps[0]).Path == "test.csv" &&
            ((ExcelOutputRecord)s.Definition.Steps[1]).Path == "test.parquet" &&
            ((ExcelOutputRecord)s.Definition.Steps[1]).Sheet == "Results"), _ct), Times.Once);
    }

    [TestMethod]
    public async Task Handle_When_VariableIsCircular_Then_RejectsRun()
    {
        var definition = new DefinitionRecord(1,
            [new CsvRecord("csv", "expression", [new("age", ColumnTypeRecord.Int64)], PathExpression: new TextVariableRecord("ref", "loop")),
             new ParquetRecord("out", "out.parquet")],
            [new VariableRecord("loop", new TextVariableRecord("cycle", "loop"))]);
        _pipeline = _pipeline with { Document = _pipeline.Document with { Definition = definition } };
        _queries.Setup(q => q.GetPipeline(_pipeline.Id, _ct)).ReturnsAsync(_pipeline);

        var result = await _sut.Handle(new(_pipeline.Id, 2, "test"), _ct);

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("cycle");
        _repository.Verify(r => r.Enqueue(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<RunSnapshotRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
