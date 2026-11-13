using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Schedules;

public sealed record DeleteScheduleInput(Guid Id, int ExpectedRevision) : ITransactionalInput;
public sealed record DeleteScheduleOutput(bool Deleted);
public sealed class DeleteScheduleCommand(IScheduleRepository repository, IScheduleTriggers triggers) : IUseCase<DeleteScheduleInput, DeleteScheduleOutput>
{
    public async Task<ErrorOr<DeleteScheduleOutput>> Handle(DeleteScheduleInput input, CancellationToken ct)
    {
        var deleted = await repository.Delete(input.Id, input.ExpectedRevision, ct);
        if (deleted.IsError) return deleted.Errors;
        await triggers.Remove(input.Id, ct);
        return new DeleteScheduleOutput(true);
    }
}
public sealed class DeleteScheduleValidator : AbstractValidator<DeleteScheduleInput>
{
    public DeleteScheduleValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ExpectedRevision).GreaterThan(0);
    }
}
