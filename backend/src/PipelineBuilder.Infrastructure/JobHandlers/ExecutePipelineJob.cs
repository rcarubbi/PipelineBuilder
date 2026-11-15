using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Runs;
using TickerQ.Utilities.Base;
using TickerQ.Utilities.Enums;

namespace PipelineBuilder.Infrastructure.JobHandlers;

public sealed partial class ExecutePipelineJob(IServiceScopeFactory scopeFactory, PipelineWorkerState state, IOptions<PipelineWorkerOptions> options, ILogger<ExecutePipelineJob> logger)
{
    [TickerFunction("ExecutePipeline", TickerTaskPriority.LongRunning)]
    public async Task Execute(CancellationToken ct)
    {
        if (!state.TryStart()) return;
        try { await Task.WhenAll(Enumerable.Range(0, options.Value.PoolSize).Select(_ => Run(ct))); }
        finally { state.Stop(); }
    }

    private async Task Run(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var executed = false;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<IUseCase<ExecuteNextRunInput, ExecuteNextRunOutput>>().Handle(new(), ct);
                if (result.IsError) Log.Rejected(logger, string.Join("; ", result.Errors.Select(error => error.Description)));
                else executed = result.Value.Executed;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                Log.Failed(logger, ex);
            }
            // Keep the TickerQ job alive; idle runners poll without rescheduling tickers.
            if (!executed) await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Pipeline worker encountered an infrastructure failure.")]
        public static partial void Failed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Error, Message = "Pipeline worker rejected execution: {Reason}")]
        public static partial void Rejected(ILogger logger, string reason);
    }
}
