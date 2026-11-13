using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Infrastructure.JobHandlers;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Models.Ticker;

namespace PipelineBuilder.Infrastructure.Scheduling;

/// <summary>Registers one TickerQ cron ticker per active schedule. PostgreSQL remains the source of truth; registrations are rebuilt at startup.</summary>
public sealed class TickerScheduleTriggers(IServiceScopeFactory scopeFactory) : IScheduleTriggers, IDisposable
{
    private readonly ConcurrentDictionary<Guid, Guid> _tickers = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task Apply(ScheduleRecord schedule, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTicker>>();
            await RemoveRegistration(manager, schedule.Id, ct);
            if (!schedule.IsActive) return;
            var added = await manager.AddAsync(new CronTicker
            {
                Function = TriggerScheduleJob.FunctionName,
                Expression = schedule.CronExpression,
                Request = TickerHelper.CreateTickerRequest(schedule.Id),
                Description = $"Schedule '{schedule.Alias}' ({schedule.Pipelines.Length} pipeline(s))"
            }, ct);
            if (!added.IsSucceded) throw new InvalidOperationException($"Could not register schedule '{schedule.Alias}'.", added.Exception);
            _tickers[schedule.Id] = added.Result.Id;
        }
        finally { _gate.Release(); }
    }

    public async Task Remove(Guid scheduleId, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await RemoveRegistration(scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTicker>>(), scheduleId, ct);
        }
        finally { _gate.Release(); }
    }

    // A missing ticker is already the desired state, so a failed delete is not an error.
    private async Task RemoveRegistration(ICronTickerManager<CronTicker> manager, Guid scheduleId, CancellationToken ct)
    {
        if (_tickers.TryRemove(scheduleId, out var tickerId)) await manager.DeleteAsync(tickerId, ct);
    }

    public void Dispose() => _gate.Dispose();
}
