using ErrorOr;
using Microsoft.Extensions.Logging;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Runs;

public sealed record TriggerScheduledRunsInput(Guid ScheduleId) : ITransactionalInput;
public sealed record TriggerScheduledRunsOutput(int Queued, int Skipped, int Rejected);
public sealed partial class TriggerScheduledRunsCommand(IScheduleQueries schedules, IDesignerQueries queries, IPipelineRepository repository,
    ILogger<TriggerScheduledRunsCommand> logger) : IUseCase<TriggerScheduledRunsInput, TriggerScheduledRunsOutput>
{
    public async Task<ErrorOr<TriggerScheduledRunsOutput>> Handle(TriggerScheduledRunsInput input, CancellationToken ct)
    {
        // The scheduler registration may be stale; the persisted schedule is authoritative.
        var schedule = await schedules.GetSchedule(input.ScheduleId, ct);
        if (schedule is not { IsActive: true }) return new TriggerScheduledRunsOutput(0, 0, 0);
        var trigger = RunTriggers.Scheduled(schedule.Alias);
        int queued = 0, skipped = 0, rejected = 0;
        foreach (var link in schedule.Pipelines)
        {
            var pipeline = await queries.GetPipeline(link.PipelineId, ct);
            if (pipeline is null) continue;
            // Overlap is per environment: the dev and test runs of one trigger must not block each other.
            if (await queries.HasActiveRun(pipeline.Id, link.Environment, ct))
            {
                Log.Skipped(logger, pipeline.Id, link.Environment, schedule.Alias);
                skipped++;
                continue;
            }
            var snapshot = RunSnapshotFactory.Create(pipeline, link.Environment, trigger);
            var run = snapshot.IsError ? snapshot.Errors : await repository.Enqueue(pipeline.Id, pipeline.Revision, snapshot.Value, ct);
            if (!run.IsError) { queued++; continue; }
            await repository.RecordRejected(pipeline.Id, pipeline.Revision, link.Environment, trigger, string.Join(" ", run.Errors.Select(e => e.Description)), ct);
            rejected++;
        }
        return new TriggerScheduledRunsOutput(queued, skipped, rejected);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Skipped scheduled run of pipeline {PipelineId} ({Environment}) by {Alias}: an execution is already queued or running.")]
        public static partial void Skipped(ILogger logger, Guid pipelineId, string environment, string alias);
    }
}
