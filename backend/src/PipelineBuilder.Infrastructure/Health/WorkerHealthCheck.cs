using Microsoft.Extensions.Diagnostics.HealthChecks;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using PipelineBuilder.Infrastructure.JobHandlers;

namespace PipelineBuilder.Infrastructure.Health;

public sealed class WorkerHealthCheck(ITickerHost host, PipelineWorkerState worker) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct)
    {
        var registered = TickerFunctionProvider.TickerFunctions.ContainsKey("ExecutePipeline");
        return Task.FromResult(registered && host.IsRunning() && worker.IsRunning
            ? HealthCheckResult.Healthy("Pipeline worker is running.")
            : HealthCheckResult.Unhealthy($"Pipeline worker unavailable (registered: {registered}, scheduler: {host.IsRunning()}, worker: {worker.IsRunning})."));
    }

}
