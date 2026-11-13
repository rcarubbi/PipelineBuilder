using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;

namespace PipelineBuilder.Infrastructure.Dashboard;

// TickerQ's UI expects numeric enums; keep its JSON isolated from the pipeline API's strict contract.
internal sealed class DashboardJsonInputFormatter(ILoggerFactory loggerFactory)
    : SystemTextJsonInputFormatter(new JsonOptions(), loggerFactory.CreateLogger<SystemTextJsonInputFormatter>())
{
    public override bool CanRead(InputFormatterContext context) =>
        context.HttpContext.Request.PathBase.StartsWithSegments("/tickerq-dashboard") && base.CanRead(context);
}

internal sealed class DashboardJsonOutputFormatter() : SystemTextJsonOutputFormatter(new JsonOptions().JsonSerializerOptions)
{
    public override bool CanWriteResult(OutputFormatterCanWriteContext context) =>
        context.HttpContext.Request.PathBase.StartsWithSegments("/tickerq-dashboard") && base.CanWriteResult(context);
}
