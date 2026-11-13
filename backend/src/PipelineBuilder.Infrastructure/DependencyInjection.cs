using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Mvc;
using TickerQ.Dashboard.DependencyInjection;
using PipelineBuilder.Infrastructure.Dashboard;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Infrastructure.Files;
using PipelineBuilder.Infrastructure.Health;
using PipelineBuilder.Infrastructure.JobHandlers;
using PipelineBuilder.Infrastructure.Persistence;
using PipelineBuilder.Infrastructure.Scheduling;
using TickerQ.DependencyInjection;

namespace PipelineBuilder.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // TickerQ 2.x's generated module initializers overwrite registrations from other assemblies.
        TickerQInstanceFactory.Initialize();
        services.AddOptions<PipelineFileOptions>().BindConfiguration("Files").ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<ServerPaths>();
        services.AddScoped<IPipelineFiles, PipelineFiles>();
        services.AddDbContext<PipelineDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Pipelines")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:Pipelines using user-secrets or environment variables.")));
        services.AddScoped<IPipelineRepository, PipelineRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDesignerQueries, DesignerQueries>();
        services.AddScoped<IScheduleRepository, ScheduleRepository>();
        services.AddScoped<IScheduleQueries, ScheduleQueries>();
        services.AddSingleton<ICronExpressions, NCrontabCronExpressions>();
        services.AddSingleton<IScheduleTriggers, TickerScheduleTriggers>();
        services.AddSingleton<PipelineWorkerState>();
        services.AddHostedService<JobInitializer>();
        services.AddTickerQ(options =>
        {
            options.SetMaxConcurrency(1);
            if (environment.IsDevelopment()) options.AddDashboard(dashboard =>
            {
                dashboard.BasePath = "/tickerq-dashboard";
                dashboard.CorsOrigins = ["http://localhost:5050"];
                dashboard.EnableBuiltInAuth = false;
            });
        });
        if (environment.IsDevelopment())
        {
            services.AddOptions<MvcOptions>().Configure<ILoggerFactory>((options, loggerFactory) =>
            {
                options.InputFormatters.Insert(0, new DashboardJsonInputFormatter(loggerFactory));
                options.OutputFormatters.Insert(0, new DashboardJsonOutputFormatter());
            });
        }
        else
        {
            services.AddControllers().ConfigureApplicationPartManager(manager =>
            {
                foreach (var part in manager.ApplicationParts.Where(part => part.Name == "TickerQ.Dashboard").ToArray())
                    manager.ApplicationParts.Remove(part);
            });
        }
        services.AddCors(options => options.AddPolicy("Designer", policy => policy.WithOrigins("http://localhost:5173").WithMethods("GET", "POST", "PUT", "DELETE").WithHeaders("Content-Type")));
        services.AddHealthChecks().AddCheck<WorkerHealthCheck>("worker", tags: ["ready"]).AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);
        return services;
    }
}
