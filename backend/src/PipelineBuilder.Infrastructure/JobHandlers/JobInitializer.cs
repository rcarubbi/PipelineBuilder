using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Schedules;
using PipelineBuilder.Infrastructure.Persistence;
using TickerQ.Utilities.Interfaces.Managers;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models.Ticker;

namespace PipelineBuilder.Infrastructure.JobHandlers;

public sealed class JobInitializer(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PipelineDbContext>();
        await db.Initialize(ct);
        // shortcut: one API/worker instance; use leases before deploying multiple workers.
        await db.Runs.Where(r => r.Status == "Running").ExecuteUpdateAsync(set => set
            .SetProperty(r => r.Status, "Interrupted").SetProperty(r => r.FinishedAt, DateTimeOffset.UtcNow)
            .SetProperty(r => r.Error, "The process stopped during execution; check output before starting another run."), ct);
        TickerQInstanceFactory.Initialize();
        var scheduled = await scope.ServiceProvider.GetRequiredService<ITimeTickerManager<TimeTicker>>().AddAsync(new TimeTicker
        {
            Function = "ExecutePipeline", ExecutionTime = DateTime.UtcNow.AddSeconds(1)
        }, ct);
        if (!scheduled.IsSucceded) throw new InvalidOperationException("Could not schedule the pipeline worker.", scheduled.Exception);
        scope.ServiceProvider.GetRequiredService<ITickerHost>().Start();
        await scope.ServiceProvider.GetRequiredService<PipelineWorkerState>().Started.WaitAsync(TimeSpan.FromSeconds(10), ct);
        // TickerQ storage is in-memory: rebuild cron registrations from persisted schedules. Missed occurrences are not replayed.
        var synced = await scope.ServiceProvider.GetRequiredService<IUseCase<SyncScheduleTriggersInput, SyncScheduleTriggersOutput>>().Handle(new(), ct);
        if (synced.IsError) throw new InvalidOperationException($"Could not restore schedules: {synced.FirstError.Description}");
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITickerHost>().Stop();
    }
}
