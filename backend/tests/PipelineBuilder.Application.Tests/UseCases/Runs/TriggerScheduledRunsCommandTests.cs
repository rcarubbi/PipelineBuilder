using System.Text.Json;
using ErrorOr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Runs;
using Shouldly;

namespace PipelineBuilder.Application.Tests.UseCases.Runs;

[TestClass]
public sealed class TriggerScheduledRunsCommandTests
{
    private readonly Mock<IScheduleQueries> _schedules = new();
    private readonly Mock<IDesignerQueries> _queries = new();
    private readonly Mock<IPipelineRepository> _repository = new();
    private TriggerScheduledRunsCommand _sut = null!;
    private SavedPipelineRecord _pipeline = null!;
    private ScheduleRecord _schedule = null!;
    private readonly CancellationToken _ct = new CancellationTokenSource().Token;
    public TriggerScheduledRunsCommandTests() => CreateSut();

    private void CreateSut()
    {
        _schedules.Reset(); _queries.Reset(); _repository.Reset();
        _pipeline = new(Guid.NewGuid(), 4, new("Customers", new(1, [new CsvRecord("csv", "${INPUT}", [new("age", ColumnTypeRecord.Int64)]), new ParquetRecord("out", "out.parquet")]),
            JsonSerializer.SerializeToElement(new { }), new() { ["test"] = new() { ["INPUT"] = "in.csv" }, ["broken"] = new() }));
        _schedule = new(Guid.NewGuid(), "nightly", "0 2 * * *", true, 1, [new(_pipeline.Id, "Customers", "test")]);
        _schedules.Setup(s => s.GetSchedule(_schedule.Id, _ct)).ReturnsAsync(() => _schedule);
        _queries.Setup(q => q.GetPipeline(_pipeline.Id, _ct)).ReturnsAsync(_pipeline);
        _repository.Setup(r => r.Enqueue(_pipeline.Id, 4, It.IsAny<RunSnapshotRecord>(), _ct))
            .ReturnsAsync(new RunRecord(Guid.NewGuid(), _pipeline.Id, 4, "test", "Queued", DateTimeOffset.UtcNow, null, null, 0, 0, null, null));
        _sut = new(_schedules.Object, _queries.Object, _repository.Object, NullLogger<TriggerScheduledRunsCommand>.Instance);
    }

    [TestMethod]
    public async Task Handle_When_ScheduleFires_Then_QueuesCurrentRevisionWithScheduledTrigger()
    {
        var result = await _sut.Handle(new(_schedule.Id), _ct);

        result.Value.ShouldBe(new TriggerScheduledRunsOutput(1, 0, 0));
        _repository.Verify(r => r.Enqueue(_pipeline.Id, 4, It.Is<RunSnapshotRecord>(s =>
            s.Trigger == "Scheduled: nightly" && s.Environment == "test" && ((CsvRecord)s.Definition.Steps[0]).Path == "in.csv"), _ct), Times.Once);
    }

    [TestMethod]
    public async Task Handle_When_PipelineAlreadyActive_Then_SkipsWithoutQueueing()
    {
        _queries.Setup(q => q.HasActiveRun(_pipeline.Id, "test", _ct)).ReturnsAsync(true);

        var result = await _sut.Handle(new(_schedule.Id), _ct);

        result.Value.ShouldBe(new TriggerScheduledRunsOutput(0, 1, 0));
        _repository.Verify(r => r.Enqueue(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<RunSnapshotRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_When_PipelineScheduledInTwoEnvironments_Then_QueuesBothAndSkipsOnlyTheActiveOne()
    {
        _pipeline.Document.Environments["dev"] = new() { ["INPUT"] = "dev.csv" };
        _schedule = _schedule with { Pipelines = [new(_pipeline.Id, "Customers", "dev"), new(_pipeline.Id, "Customers", "test")] };

        var both = await _sut.Handle(new(_schedule.Id), _ct);
        _queries.Setup(q => q.HasActiveRun(_pipeline.Id, "dev", _ct)).ReturnsAsync(true);
        var devBusy = await _sut.Handle(new(_schedule.Id), _ct);

        both.Value.ShouldBe(new TriggerScheduledRunsOutput(2, 0, 0));
        devBusy.Value.ShouldBe(new TriggerScheduledRunsOutput(1, 1, 0));
        _repository.Verify(r => r.Enqueue(_pipeline.Id, 4, It.Is<RunSnapshotRecord>(s => s.Environment == "dev" && ((CsvRecord)s.Definition.Steps[0]).Path == "dev.csv"), _ct), Times.Once);
        _repository.Verify(r => r.Enqueue(_pipeline.Id, 4, It.Is<RunSnapshotRecord>(s => s.Environment == "test" && ((CsvRecord)s.Definition.Steps[0]).Path == "in.csv"), _ct), Times.Exactly(2));
    }

    [TestMethod]
    public async Task Handle_When_EnvironmentVariableMissing_Then_RecordsVisibleFailure()
    {
        _schedule = _schedule with { Pipelines = [new(_pipeline.Id, "Customers", "broken")] };

        var result = await _sut.Handle(new(_schedule.Id), _ct);

        result.Value.ShouldBe(new TriggerScheduledRunsOutput(0, 0, 1));
        _repository.Verify(r => r.RecordRejected(_pipeline.Id, 4, "broken", "Scheduled: nightly", "Missing environment variable 'INPUT'.", _ct), Times.Once);
        _repository.Verify(r => r.Enqueue(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<RunSnapshotRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_When_EnqueueConflicts_Then_RecordsVisibleFailure()
    {
        _repository.Setup(r => r.Enqueue(_pipeline.Id, 4, It.IsAny<RunSnapshotRecord>(), _ct)).ReturnsAsync(Error.Conflict("Pipeline.Revision", "Pipeline changed."));

        var result = await _sut.Handle(new(_schedule.Id), _ct);

        result.Value.Rejected.ShouldBe(1);
        _repository.Verify(r => r.RecordRejected(_pipeline.Id, 4, "test", "Scheduled: nightly", "Pipeline changed.", _ct), Times.Once);
    }

    [TestMethod]
    public async Task Handle_When_ScheduleDisabledOrMissing_Then_DoesNothing()
    {
        _schedule = _schedule with { IsEnabled = false };

        var disabled = await _sut.Handle(new(_schedule.Id), _ct);
        var missing = await _sut.Handle(new(Guid.NewGuid()), _ct);

        disabled.Value.ShouldBe(new TriggerScheduledRunsOutput(0, 0, 0));
        missing.Value.ShouldBe(new TriggerScheduledRunsOutput(0, 0, 0));
        _queries.Verify(q => q.GetPipeline(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
