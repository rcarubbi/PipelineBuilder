using ErrorOr;
using Microsoft.Extensions.Logging;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Runs;

public sealed record ExecuteNextRunInput : IInput;
public sealed record ExecuteNextRunOutput(bool Executed);
public sealed partial class ExecuteNextRunCommand(IPipelineRepository repository, IPipelineFiles files, ILogger<ExecuteNextRunCommand> logger) : IUseCase<ExecuteNextRunInput, ExecuteNextRunOutput>
{
    public async Task<ErrorOr<ExecuteNextRunOutput>> Handle(ExecuteNextRunInput input, CancellationToken ct)
    {
        var run = await repository.ClaimNext(ct);
        if (run is null) return new ExecuteNextRunOutput(false);
        try
        {
            var result = await files.Execute(run.Snapshot.Definition, token => repository.IsCancellationRequested(run.Id, token), ct);
            await repository.Complete(run.Id, "Succeeded", result, null, null, ct);
        }
        catch (PipelineDataException ex)
        {
            await repository.Complete(run.Id, "Failed", null, ex.Message, ex.BlockId, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await repository.Complete(run.Id, "Cancelled", null, null, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Failed(logger, run.Id, ex);
            await repository.Complete(run.Id, "Failed", null, "An infrastructure failure interrupted execution. Consult server logs using the run ID.", null, ct);
        }
        return new ExecuteNextRunOutput(true);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Execution failed for run {RunId}.")]
        public static partial void Failed(ILogger logger, Guid runId, Exception exception);
    }
}
