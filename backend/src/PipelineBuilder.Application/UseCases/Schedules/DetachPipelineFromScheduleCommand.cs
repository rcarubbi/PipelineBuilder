using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Schedules;

public sealed record DetachPipelineFromScheduleInput(string Alias, Guid PipelineId, string Environment) : ITransactionalInput;
public sealed record DetachPipelineFromScheduleOutput(ScheduleRecord Schedule);
public sealed class DetachPipelineFromScheduleCommand(IScheduleQueries schedules, IScheduleRepository repository, IScheduleTriggers triggers)
    : IUseCase<DetachPipelineFromScheduleInput, DetachPipelineFromScheduleOutput>
{
    public async Task<ErrorOr<DetachPipelineFromScheduleOutput>> Handle(DetachPipelineFromScheduleInput input, CancellationToken ct)
    {
        var schedule = await schedules.GetScheduleByAlias(input.Alias, ct);
        if (schedule is null) return Error.NotFound("Schedule.NotFound", $"Schedule '{input.Alias}' not found.");
        if (!await repository.Detach(schedule.Id, input.PipelineId, input.Environment, ct))
            return Error.NotFound("Schedule.PipelineNotAttached", $"The pipeline is not attached to this schedule for environment '{input.Environment}'.");
        var updated = (await schedules.GetSchedule(schedule.Id, ct))!;
        await triggers.Apply(updated, ct);
        return new DetachPipelineFromScheduleOutput(updated);
    }
}
public sealed class DetachPipelineFromScheduleValidator : AbstractValidator<DetachPipelineFromScheduleInput>
{
    public DetachPipelineFromScheduleValidator()
    {
        RuleFor(x => x.Alias).ScheduleAlias();
        RuleFor(x => x.PipelineId).NotEmpty();
        RuleFor(x => x.Environment).NotEmpty();
    }
}
