using System.Text.Json;
using System.Text.Json.Serialization;
using Asp.Versioning;
using TickerQ.Dashboard.Controllers;
using PipelineBuilder.Presentation.Middleware;

namespace PipelineBuilder.Presentation;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.JsonSerializerOptions.RespectRequiredConstructorParameters = true;
            options.JsonSerializerOptions.RespectNullableAnnotations = true;
            options.JsonSerializerOptions.AllowOutOfOrderMetadataProperties = true;
            options.JsonSerializerOptions.MaxDepth = 48;
        });
        services.AddApiVersioning(options =>
        {
            options.ReportApiVersions = true;
            options.ApiVersionReader = new UrlSegmentApiVersionReader();
        }).AddMvc(options => options.Conventions.Controller<TickerQController>().IsApiVersionNeutral()).AddApiExplorer(options =>
        {
            options.GroupNameFormat = "'v'V";
            options.SubstituteApiVersionInUrl = true;
        });
        services.AddSwaggerGen();
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context => context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }
}
