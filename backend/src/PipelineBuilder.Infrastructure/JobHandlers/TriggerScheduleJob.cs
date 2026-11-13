using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Runs;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Models;

namespace PipelineBuilder.Infrastructure.JobHandlers;

public sealed partial class TriggerScheduleJob(IServiceScopeFactory scopeFactory, ILogger<TriggerScheduleJob> logger)
{
    public const string FunctionName = "TriggerSchedule";

    [TickerFunction("TriggerSchedule")]
    public async Task Execute(TickerFunctionContext<Guid> context, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IUseCase<TriggerScheduledRunsInput, TriggerScheduledRunsOutput>>()
            .Handle(new(context.Request), ct);
        if (result.IsError) Log.Rejected(logger, context.Request, string.Join("; ", result.Errors.Select(e => e.Description)));
        else Log.Triggered(logger, context.Request, result.Value.Queued, result.Value.Skipped, result.Value.Rejected);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Schedule {ScheduleId} fired: {Queued} queued, {Skipped} skipped, {Rejected} rejected.")]
        public static partial void Triggered(ILogger logger, Guid scheduleId, int queued, int skipped, int rejected);

        [LoggerMessage(Level = LogLevel.Error, Message = "Schedule {ScheduleId} could not trigger runs: {Reason}")]
        public static partial void Rejected(ILogger logger, Guid scheduleId, string reason);
    }
}
