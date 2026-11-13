using PipelineBuilder.Application;
using PipelineBuilder.Infrastructure;
using PipelineBuilder.Presentation;
using Serilog;
using TickerQ.DependencyInjection;
using TickerQ.Utilities.Enums;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, configuration) => configuration.ReadFrom.Configuration(context.Configuration).WriteTo.Console());
builder.Services.AddPresentation().AddApplication().AddInfrastructure(builder.Configuration, builder.Environment);
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseRouting();
app.UseCors("Designer");
app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });
app.UseTickerQ(TickerQStartMode.Manual);
await app.RunAsync();

public partial class Program
{
    private Program() { }
}
