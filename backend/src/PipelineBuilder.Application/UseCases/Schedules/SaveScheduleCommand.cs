using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Schedules;

public sealed record SaveScheduleInput(Guid Id, int ExpectedRevision, string Alias, string CronExpression, bool IsEnabled) : ITransactionalInput;
public sealed record SaveScheduleOutput(ScheduleRecord Schedule);
public sealed class SaveScheduleCommand(IScheduleRepository repository, IScheduleQueries queries, IScheduleTriggers triggers) : IUseCase<SaveScheduleInput, SaveScheduleOutput>
{
    public async Task<ErrorOr<SaveScheduleOutput>> Handle(SaveScheduleInput input, CancellationToken ct)
    {
        var saved = await repository.Save(input.Id, input.ExpectedRevision, input.Alias.Trim(), input.CronExpression.Trim(), input.IsEnabled, ct);
        if (saved.IsError) return saved.Errors;
        var schedule = (await queries.GetSchedule(input.Id, ct))!;
        await triggers.Apply(schedule, ct);
        return new SaveScheduleOutput(schedule);
    }
}
public sealed class SaveScheduleValidator : AbstractValidator<SaveScheduleInput>
{
    public SaveScheduleValidator(ICronExpressions cron)
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ExpectedRevision).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Alias).ScheduleAlias();
        RuleFor(x => x.CronExpression).CronExpression(cron);
    }
}
