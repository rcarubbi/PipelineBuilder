using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Schedules;

public sealed record AttachPipelineToScheduleInput(string Alias, Guid PipelineId, string Environment) : ITransactionalInput;
public sealed record AttachPipelineToScheduleOutput(ScheduleRecord Schedule);
public sealed class AttachPipelineToScheduleCommand(IScheduleQueries schedules, IDesignerQueries pipelines, IScheduleRepository repository,
    IScheduleTriggers triggers) : IUseCase<AttachPipelineToScheduleInput, AttachPipelineToScheduleOutput>
{
    public async Task<ErrorOr<AttachPipelineToScheduleOutput>> Handle(AttachPipelineToScheduleInput input, CancellationToken ct)
    {
        var schedule = await schedules.GetScheduleByAlias(input.Alias, ct);
        if (schedule is null) return Error.NotFound("Schedule.NotFound", $"Schedule '{input.Alias}' not found.");
        var pipeline = await pipelines.GetPipeline(input.PipelineId, ct);
        if (pipeline is null) return Error.NotFound("Pipeline.NotFound", "Pipeline not found.");
        if (!pipeline.Document.Environments.ContainsKey(input.Environment))
            return Error.Validation("Environment", $"Pipeline '{pipeline.Document.Name}' has no environment '{input.Environment}'.");
        await repository.Attach(schedule.Id, pipeline.Id, input.Environment, ct);
        var updated = (await schedules.GetSchedule(schedule.Id, ct))!;
        await triggers.Apply(updated, ct);
        return new AttachPipelineToScheduleOutput(updated);
    }
}
public sealed class AttachPipelineToScheduleValidator : AbstractValidator<AttachPipelineToScheduleInput>
{
    public AttachPipelineToScheduleValidator()
    {
        RuleFor(x => x.Alias).ScheduleAlias();
        RuleFor(x => x.PipelineId).NotEmpty();
        RuleFor(x => x.Environment).NotEmpty();
    }
}
