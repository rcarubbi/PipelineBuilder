using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PipelineBuilder.Presentation.Middleware;

public sealed partial class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        Log.Unhandled(logger, exception);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = 500, Title = "The operation failed. Consult server logs using the trace ID." },
            Exception = exception
        });
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "API request failed.")]
        public static partial void Unhandled(ILogger logger, Exception exception);
    }
}
