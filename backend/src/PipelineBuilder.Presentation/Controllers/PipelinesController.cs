using Microsoft.AspNetCore.Mvc;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Pipelines;
using PipelineBuilder.Presentation.Models;

namespace PipelineBuilder.Presentation.Controllers;

[Route("api/v{version:apiVersion}/pipelines")]
public sealed class PipelinesController(
    IUseCase<SavePipelineInput, SavePipelineOutput> save,
    IUseCase<GetPipelineInput, GetPipelineOutput> get,
    IUseCase<ValidatePipelineInput, ValidatePipelineOutput> validate,
    IUseCase<ListPipelinesInput, ListPipelinesOutput> list,
    IUseCase<DeletePipelineInput, DeletePipelineOutput> delete,
    IUseCase<ImportPipelineInput, ImportPipelineOutput> import) : ApiController
{
    [HttpPost("import")]
    public async Task<ActionResult<PipelineResponse>> Import(DocumentModel request, CancellationToken ct)
    {
        var result = await import.Handle(new(request.MapTo()), ct);
        if (result.IsError) return Respond(result, output => PipelineResponse.MapFrom(output.Pipeline));
        return Created($"/api/v1/pipelines/{result.Value.Pipeline.Id}", PipelineResponse.MapFrom(result.Value.Pipeline));
    }

    [HttpGet]
    public async Task<ActionResult<PipelineListResponse>> List(CancellationToken ct, [FromQuery] int page = 1) =>
        Respond(await list.Handle(new(page), ct), output => new PipelineListResponse(output.Pipelines.Select(PipelineSummaryResponse.MapFrom).ToArray(), output.Page, output.HasMore, output.TotalCount));

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<DeletePipelineResponse>> Delete(Guid id, [FromQuery] int revision, CancellationToken ct) =>
        Respond(await delete.Handle(new(id, revision), ct), output => new DeletePipelineResponse(output.Deleted));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PipelineResponse>> Save(Guid id, SavePipelineRequest request, CancellationToken ct) =>
        Respond(await save.Handle(new(id, request.ExpectedRevision, request.Document.MapTo()), ct), output => PipelineResponse.MapFrom(output.Pipeline));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PipelineResponse>> Get(Guid id, CancellationToken ct) =>
        Respond(await get.Handle(new(id), ct), output => PipelineResponse.MapFrom(output.Pipeline));

    [HttpPost("validate")]
    public async Task<ActionResult<AnalysisResponse>> Validate(DefinitionModel request, CancellationToken ct) =>
        Respond(await validate.Handle(new(request.MapTo()), ct), output => AnalysisResponse.MapFrom(output.Analysis));
}
