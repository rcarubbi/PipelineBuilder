using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Pipelines;

public sealed record DeletePipelineInput(Guid Id, int ExpectedRevision) : ITransactionalInput;
public sealed record DeletePipelineOutput(bool Deleted);
public sealed class DeletePipelineCommand(IPipelineRepository repository, IScheduleQueries schedules, IScheduleTriggers triggers) : IUseCase<DeletePipelineInput, DeletePipelineOutput>
{
    public async Task<ErrorOr<DeletePipelineOutput>> Handle(DeletePipelineInput input, CancellationToken ct)
    {
        var affected = (await schedules.GetSchedules(ct)).Where(s => s.Pipelines.Any(p => p.PipelineId == input.Id)).Select(s => s.Id).ToArray();
        var result = await repository.Delete(input.Id, input.ExpectedRevision, ct);
        if (result.IsError) return result.Errors;
        foreach (var id in affected)
            if (await schedules.GetSchedule(id, ct) is { } schedule) await triggers.Apply(schedule, ct);
        return new DeletePipelineOutput(result.Value);
    }
}
public sealed class DeletePipelineValidator : AbstractValidator<DeletePipelineInput>
{
    public DeletePipelineValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ExpectedRevision).GreaterThan(0);
    }
}
