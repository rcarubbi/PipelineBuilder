using ErrorOr;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Schedules;

public sealed record ListSchedulesInput : IInput;
public sealed record ScheduleSummaryRecord(ScheduleRecord Schedule, DateTimeOffset[] NextRuns);
public sealed record ListSchedulesOutput(ScheduleSummaryRecord[] Schedules);
public sealed class ListSchedulesQuery(IScheduleQueries queries, ICronExpressions cron, TimeProvider clock) : IUseCase<ListSchedulesInput, ListSchedulesOutput>
{
    public const int PreviewCount = 3;

    public async Task<ErrorOr<ListSchedulesOutput>> Handle(ListSchedulesInput input, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var schedules = await queries.GetSchedules(ct);
        return new ListSchedulesOutput(schedules.Select(schedule => new ScheduleSummaryRecord(schedule,
            schedule.IsActive ? cron.GetNextOccurrences(schedule.CronExpression, now, PreviewCount) : [])).ToArray());
    }
}
