using Asp.Versioning;
using ErrorOr;
using Microsoft.AspNetCore.Mvc;

namespace PipelineBuilder.Presentation.Controllers;

[ApiController]
[ApiVersion(1.0)]
[RequestSizeLimit(1_048_576)]
[Produces("application/json")]
public abstract class ApiController : ControllerBase
{
    protected ActionResult<TResponse> Respond<TOutput, TResponse>(ErrorOr<TOutput> result, Func<TOutput, TResponse> map)
    {
        if (!result.IsError) return Ok(map(result.Value));
        var first = result.FirstError;
        var status = first.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        return Problem(statusCode: status, title: first.Description, extensions: new Dictionary<string, object?>
        {
            ["errors"] = result.Errors.Select(e => new { code = e.Code, message = e.Description }).ToArray()
        });
    }
}
