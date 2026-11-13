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
}
