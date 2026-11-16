using Microsoft.AspNetCore.Mvc;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Runs;
using PipelineBuilder.Presentation.Models;

namespace PipelineBuilder.Presentation.Controllers;

[Route("api/v{version:apiVersion}/runs")]
public sealed class RunsController(IUseCase<StartRunInput, StartRunOutput> start, IUseCase<GetRunInput, GetRunOutput> get,
    IUseCase<CancelRunInput, CancelRunOutput> cancel, IUseCase<GetRunHistoryInput, GetRunHistoryOutput> history) : ApiController
{
    [HttpGet]
    public async Task<ActionResult<RunHistoryResponse>> History(CancellationToken ct, [FromQuery] int page = 1, [FromQuery] Guid? pipelineId = null,
        [FromQuery] int pageSize = GetRunHistoryQuery.DefaultPageSize) =>
        Respond(await history.Handle(new(page, pipelineId, pageSize), ct), output => new RunHistoryResponse(output.Runs.Select(RunResponse.MapFrom).ToArray(), output.Page, output.HasMore, output.TotalCount));

    [HttpPost]
    public async Task<ActionResult<RunResponse>> Start(StartRunRequest request, CancellationToken ct)
    {
        var result = await start.Handle(new(request.PipelineId, request.Revision, request.Environment), ct);
        if (result.IsError) return Respond(result, output => RunResponse.MapFrom(output.Run));
        return Accepted($"/api/v1/runs/{result.Value.Run.Id}", RunResponse.MapFrom(result.Value.Run));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<RunResponse>> Get(Guid id, CancellationToken ct) =>
        Respond(await get.Handle(new(id), ct), output => RunResponse.MapFrom(output.Run));

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<object>> Cancel(Guid id, CancellationToken ct) =>
        Respond(await cancel.Handle(new(id), ct), output => (object)new { output.CancellationRequested });
}
