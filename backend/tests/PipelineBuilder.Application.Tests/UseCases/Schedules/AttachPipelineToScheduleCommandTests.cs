using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Schedules;
using Shouldly;

namespace PipelineBuilder.Application.Tests.UseCases.Schedules;

[TestClass]
public sealed class AttachPipelineToScheduleCommandTests
{
    private readonly Mock<IScheduleQueries> _schedules = new();
    private readonly Mock<IDesignerQueries> _pipelines = new();
    private readonly Mock<IScheduleRepository> _repository = new();
    private readonly Mock<IScheduleTriggers> _triggers = new();
    private AttachPipelineToScheduleCommand _sut = null!;
    private ScheduleRecord _schedule = null!;
    private SavedPipelineRecord _pipeline = null!;
    private readonly CancellationToken _ct = new CancellationTokenSource().Token;
    public AttachPipelineToScheduleCommandTests() => CreateSut();

    private void CreateSut()
    {
        _schedules.Reset(); _pipelines.Reset(); _repository.Reset(); _triggers.Reset();
        _schedule = new(Guid.NewGuid(), "Daily", "0 8 * * *", true, 1, []);
        _pipeline = new(Guid.NewGuid(), 2, new("Customers", new(1, []), JsonSerializer.SerializeToElement(new { }), new() { ["test"] = new() }));
        _schedules.Setup(s => s.GetScheduleByAlias("daily", _ct)).ReturnsAsync(_schedule);
        _pipelines.Setup(p => p.GetPipeline(_pipeline.Id, _ct)).ReturnsAsync(_pipeline);
        _sut = new(_schedules.Object, _pipelines.Object, _repository.Object, _triggers.Object);
    }

    [TestMethod]
    public async Task Handle_When_PipelineAttached_Then_RegistersUpdatedSchedule()
    {
        var updated = _schedule with { Pipelines = [new(_pipeline.Id, "Customers", "test")] };
        _schedules.Setup(s => s.GetSchedule(_schedule.Id, _ct)).ReturnsAsync(updated);

        var result = await _sut.Handle(new("daily", _pipeline.Id, "test"), _ct);

        result.Value.Schedule.ShouldBe(updated);
        _repository.Verify(r => r.Attach(_schedule.Id, _pipeline.Id, "test", _ct), Times.Once);
        _triggers.Verify(t => t.Apply(updated, _ct), Times.Once);
    }

    [TestMethod]
    public async Task Handle_When_EnvironmentUnknown_Then_RejectsWithoutScheduling()
    {
        var result = await _sut.Handle(new("daily", _pipeline.Id, "production"), _ct);

        result.FirstError.Type.ShouldBe(ErrorOr.ErrorType.Validation);
        _repository.VerifyNoOtherCalls();
        _triggers.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Handle_When_AliasUnknown_Then_ReturnsNotFound()
    {
        var result = await _sut.Handle(new("weekly", _pipeline.Id, "test"), _ct);

        result.FirstError.Type.ShouldBe(ErrorOr.ErrorType.NotFound);
        _repository.VerifyNoOtherCalls();
    }
}
