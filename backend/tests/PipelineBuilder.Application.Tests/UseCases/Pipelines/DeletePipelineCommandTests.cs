using ErrorOr;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Pipelines;
using Shouldly;

namespace PipelineBuilder.Application.Tests.UseCases.Pipelines;

[TestClass]
public sealed class DeletePipelineCommandTests
{
    private readonly Mock<IPipelineRepository> _repository = new();
    private readonly Mock<IScheduleQueries> _schedules = new();
    private readonly Mock<IScheduleTriggers> _triggers = new();
    private DeletePipelineCommand _sut = null!;
    private readonly CancellationToken _ct = new CancellationTokenSource().Token;
    public DeletePipelineCommandTests() => CreateSut();

    private void CreateSut()
    {
        _repository.Reset(); _schedules.Reset(); _triggers.Reset();
        _schedules.Setup(s => s.GetSchedules(_ct)).ReturnsAsync([]);
        _sut = new(_repository.Object, _schedules.Object, _triggers.Object);
    }

    [TestMethod]
    public async Task Handle_When_RepositoryRejectsDeletion_Then_PreservesConflict()
    {
        var id = Guid.NewGuid();
        _repository.Setup(r => r.Delete(id, 3, _ct)).ReturnsAsync(Error.Conflict("Pipeline.ActiveRuns", "Pipeline has active runs."));

        var result = await _sut.Handle(new(id, 3), _ct);

        result.FirstError.Code.ShouldBe("Pipeline.ActiveRuns");
        _triggers.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Handle_When_DeletionSucceeds_Then_ReturnsDeleted()
    {
        var id = Guid.NewGuid();
        _repository.Setup(r => r.Delete(id, 3, _ct)).ReturnsAsync(true);

        var result = await _sut.Handle(new(id, 3), _ct);

        result.Value.Deleted.ShouldBeTrue();
    }

    [TestMethod]
    public async Task Handle_When_PipelineWasScheduled_Then_ReappliesAffectedSchedules()
    {
        var id = Guid.NewGuid();
        var before = new ScheduleRecord(Guid.NewGuid(), "daily", "0 8 * * *", true, 1, [new(id, "Customers", "test")]);
        var after = before with { Pipelines = [] };
        _schedules.Setup(s => s.GetSchedules(_ct)).ReturnsAsync([before, new ScheduleRecord(Guid.NewGuid(), "other", "0 9 * * *", true, 1, [])]);
        _schedules.Setup(s => s.GetSchedule(before.Id, _ct)).ReturnsAsync(after);
        _repository.Setup(r => r.Delete(id, 3, _ct)).ReturnsAsync(true);

        await _sut.Handle(new(id, 3), _ct);

        _triggers.Verify(t => t.Apply(after, _ct), Times.Once);
        _triggers.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task ValidateAsync_When_IdOrRevisionMissing_Then_RejectsRequest()
    {
        var result = await new DeletePipelineValidator().ValidateAsync(new DeletePipelineInput(Guid.Empty, 0), _ct);

        result.IsValid.ShouldBeFalse();
    }
}
