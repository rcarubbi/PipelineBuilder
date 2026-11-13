using ErrorOr;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Schedules;

public sealed record SyncScheduleTriggersInput : IInput;
public sealed record SyncScheduleTriggersOutput(int Active);
/// <summary>Rebuilds scheduler registrations from persisted schedules, for example after a process restart.</summary>
public sealed class SyncScheduleTriggersCommand(IScheduleQueries queries, IScheduleTriggers triggers) : IUseCase<SyncScheduleTriggersInput, SyncScheduleTriggersOutput>
{
    public async Task<ErrorOr<SyncScheduleTriggersOutput>> Handle(SyncScheduleTriggersInput input, CancellationToken ct)
    {
        var schedules = await queries.GetSchedules(ct);
        foreach (var schedule in schedules) await triggers.Apply(schedule, ct);
        return new SyncScheduleTriggersOutput(schedules.Count(s => s.IsActive));
    }
}
