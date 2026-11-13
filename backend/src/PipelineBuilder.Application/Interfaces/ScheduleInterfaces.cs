using ErrorOr;
using PipelineBuilder.Application.Contracts;

namespace PipelineBuilder.Application.Interfaces;

public interface IScheduleRepository
{
    Task<ErrorOr<int>> Save(Guid id, int expectedRevision, string alias, string cronExpression, bool isEnabled, CancellationToken ct);
    Task<ErrorOr<bool>> Delete(Guid id, int expectedRevision, CancellationToken ct);
    Task Attach(Guid scheduleId, Guid pipelineId, string environment, CancellationToken ct);
    Task<bool> Detach(Guid scheduleId, Guid pipelineId, string environment, CancellationToken ct);
}

public interface IScheduleQueries
{
    Task<ScheduleRecord[]> GetSchedules(CancellationToken ct);
    Task<ScheduleRecord?> GetSchedule(Guid id, CancellationToken ct);
    Task<ScheduleRecord?> GetScheduleByAlias(string alias, CancellationToken ct);
}

/// <summary>Keeps the job scheduler's cron registrations in line with persisted schedules.</summary>
public interface IScheduleTriggers
{
    Task Apply(ScheduleRecord schedule, CancellationToken ct);
    Task Remove(Guid scheduleId, CancellationToken ct);
}

public interface ICronExpressions
{
    bool IsValid(string expression);
    DateTimeOffset[] GetNextOccurrences(string expression, DateTimeOffset after, int count);
}
