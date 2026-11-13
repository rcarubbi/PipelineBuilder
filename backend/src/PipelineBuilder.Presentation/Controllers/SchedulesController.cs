using Microsoft.AspNetCore.Mvc;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Schedules;
using PipelineBuilder.Presentation.Models;

namespace PipelineBuilder.Presentation.Controllers;

[Route("api/v{version:apiVersion}/schedules")]
public sealed class SchedulesController(
    IUseCase<ListSchedulesInput, ListSchedulesOutput> list,
    IUseCase<SaveScheduleInput, SaveScheduleOutput> save,
    IUseCase<DeleteScheduleInput, DeleteScheduleOutput> delete,
    IUseCase<PreviewScheduleInput, PreviewScheduleOutput> preview,
    IUseCase<AttachPipelineToScheduleInput, AttachPipelineToScheduleOutput> attach,
    IUseCase<DetachPipelineFromScheduleInput, DetachPipelineFromScheduleOutput> detach) : ApiController
{
    [HttpGet]
    public async Task<ActionResult<ScheduleListResponse>> List(CancellationToken ct) =>
        Respond(await list.Handle(new(), ct), output => new ScheduleListResponse(output.Schedules.Select(ScheduleResponse.MapFrom).ToArray()));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ScheduleResponse>> Save(Guid id, SaveScheduleRequest request, CancellationToken ct) =>
        Respond(await save.Handle(new(id, request.ExpectedRevision, request.Alias, request.CronExpression, request.IsEnabled), ct),
            output => ScheduleResponse.MapFrom(output.Schedule, []));

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<DeleteScheduleResponse>> Delete(Guid id, [FromQuery] int revision, CancellationToken ct) =>
        Respond(await delete.Handle(new(id, revision), ct), output => new DeleteScheduleResponse(output.Deleted));

    [HttpPost("preview")]
    public async Task<ActionResult<PreviewScheduleResponse>> Preview(PreviewScheduleRequest request, CancellationToken ct) =>
        Respond(await preview.Handle(new(request.CronExpression), ct), output => new PreviewScheduleResponse(output.NextRuns));

    [HttpPut("by-alias/{alias}/pipelines/{pipelineId:guid}/environments/{environment}")]
    public async Task<ActionResult<ScheduleResponse>> Attach(string alias, Guid pipelineId, string environment, CancellationToken ct) =>
        Respond(await attach.Handle(new(alias, pipelineId, environment), ct), output => ScheduleResponse.MapFrom(output.Schedule, []));

    [HttpDelete("by-alias/{alias}/pipelines/{pipelineId:guid}/environments/{environment}")]
    public async Task<ActionResult<ScheduleResponse>> Detach(string alias, Guid pipelineId, string environment, CancellationToken ct) =>
        Respond(await detach.Handle(new(alias, pipelineId, environment), ct), output => ScheduleResponse.MapFrom(output.Schedule, []));
}
