using ErrorOr;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Runs;

public sealed record CancelRunInput(Guid Id) : ITransactionalInput;
public sealed record CancelRunOutput(bool CancellationRequested);
public sealed class CancelRunCommand(IPipelineRepository repository) : IUseCase<CancelRunInput, CancelRunOutput>
{
    public async Task<ErrorOr<CancelRunOutput>> Handle(CancelRunInput input, CancellationToken ct) =>
        await repository.Cancel(input.Id, ct) ? new CancelRunOutput(true) : Error.Conflict("Run.Cancel", "Run is absent or already finished.");
}
