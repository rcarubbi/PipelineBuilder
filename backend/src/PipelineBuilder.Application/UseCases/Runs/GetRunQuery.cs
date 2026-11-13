using ErrorOr;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Runs;

public sealed record GetRunInput(Guid Id) : IInput;
public sealed record GetRunOutput(RunRecord Run);
public sealed class GetRunQuery(IDesignerQueries queries) : IUseCase<GetRunInput, GetRunOutput>
{
    public async Task<ErrorOr<GetRunOutput>> Handle(GetRunInput input, CancellationToken ct) =>
        await queries.GetRun(input.Id, ct) is { } run ? new GetRunOutput(run) : Error.NotFound("Run.NotFound", "Run not found.");
}
