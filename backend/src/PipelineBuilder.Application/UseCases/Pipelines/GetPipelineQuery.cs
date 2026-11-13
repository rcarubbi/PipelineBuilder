using ErrorOr;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Pipelines;

public sealed record GetPipelineInput(Guid Id) : IInput;
public sealed record GetPipelineOutput(SavedPipelineRecord Pipeline);
public sealed class GetPipelineQuery(IDesignerQueries queries) : IUseCase<GetPipelineInput, GetPipelineOutput>
{
    public async Task<ErrorOr<GetPipelineOutput>> Handle(GetPipelineInput input, CancellationToken ct) =>
        await queries.GetPipeline(input.Id, ct) is { } pipeline ? new GetPipelineOutput(pipeline) : Error.NotFound("Pipeline.NotFound", "Pipeline not found.");
}
