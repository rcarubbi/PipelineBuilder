using Microsoft.AspNetCore.Mvc;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Files;
using PipelineBuilder.Presentation.Models;

namespace PipelineBuilder.Presentation.Controllers;

[Route("api/v{version:apiVersion}/schemas")]
public sealed class SchemasController(IUseCase<DiscoverSchemaInput, DiscoverSchemaOutput> discover) : ApiController
{
    [HttpPost("discover")]
    public async Task<ActionResult<DiscoverSchemaResponse>> Discover(DiscoverSchemaRequest request, CancellationToken ct) =>
        Respond(await discover.Handle(new(request.Path, request.Delimiter, request.HasHeader), ct), output => new DiscoverSchemaResponse(output.Schema.Select(ColumnModel.MapFrom).ToArray(), output.SampleLimit));
}
