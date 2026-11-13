using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Schedules;

public sealed record PreviewScheduleInput(string CronExpression) : IInput;
public sealed record PreviewScheduleOutput(DateTimeOffset[] NextRuns);
public sealed class PreviewScheduleQuery(ICronExpressions cron, TimeProvider clock) : IUseCase<PreviewScheduleInput, PreviewScheduleOutput>
{
    public const int PreviewCount = 5;

    public Task<ErrorOr<PreviewScheduleOutput>> Handle(PreviewScheduleInput input, CancellationToken ct) =>
        Task.FromResult<ErrorOr<PreviewScheduleOutput>>(new PreviewScheduleOutput(cron.GetNextOccurrences(input.CronExpression.Trim(), clock.GetUtcNow(), PreviewCount)));
}
public sealed class PreviewScheduleValidator : AbstractValidator<PreviewScheduleInput>
{
    public PreviewScheduleValidator(ICronExpressions cron) => RuleFor(x => x.CronExpression).CronExpression(cron);
}
