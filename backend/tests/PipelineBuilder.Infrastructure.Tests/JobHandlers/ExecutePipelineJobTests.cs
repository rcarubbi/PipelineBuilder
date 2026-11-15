using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PipelineBuilder.Application;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Pipelines;
using PipelineBuilder.Application.UseCases.Runs;
using PipelineBuilder.Application.UseCases.Schedules;
using PipelineBuilder.Infrastructure.Health;
using PipelineBuilder.Infrastructure.JobHandlers;
using Shouldly;
using Testcontainers.PostgreSql;
using TickerQ.DependencyInjection;
using TickerQ.Utilities;
using TickerQ.Utilities.Enums;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models.Ticker;

namespace PipelineBuilder.Infrastructure.Tests.JobHandlers;

[TestClass]
[TestCategory("Docker")]
public sealed class ExecutePipelineJobTests
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(60));
    private readonly Guid _pipelineId = Guid.NewGuid();
    private WebApplication? _app;
    private HttpClient _client = null!;
    private PipelineWorkerState _worker = null!;
    private ExecutePipelineJob _sut = null!;
    private string _root = "";

    [TestInitialize]
    public async Task Initialize()
    {
        await _postgres.StartAsync(_timeout.Token);
        _root = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "input"));
        Directory.CreateDirectory(Path.Combine(_root, "output"));
        await File.WriteAllTextAsync(Path.Combine(_root, "input/in.csv"), "age\n32\n", _timeout.Token);
        await File.WriteAllTextAsync(Path.Combine(_root, "output/existing.parquet"), "preserve me", _timeout.Token);
    }

    private async Task StartApp(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment, ContentRootPath = _root,
            ApplicationName = typeof(ExecutePipelineJob).Assembly.GetName().Name
        });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Pipelines"] = _postgres.GetConnectionString(),
            ["Files:InputRoot"] = "input", ["Files:OutputRoot"] = "output"
        });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false)));
        builder.Services.AddApplication().AddInfrastructure(builder.Configuration, builder.Environment);
        _app = builder.Build();
        _app.UseRouting();
        _app.UseTickerQ(TickerQStartMode.Manual);
        _app.MapControllers();
        await _app.StartAsync(_timeout.Token);
        _worker = _app.Services.GetRequiredService<PipelineWorkerState>();
        CreateSut();
        _client = new HttpClient { BaseAddress = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    }

    private void CreateSut() => _sut = new(_app!.Services.GetRequiredService<IServiceScopeFactory>(), _worker, _app.Services.GetRequiredService<IOptions<PipelineWorkerOptions>>(), NullLogger<ExecutePipelineJob>.Instance);

    [TestCleanup]
    public async Task Cleanup()
    {
        _client?.Dispose();
        if (_app is not null) await _app.DisposeAsync();
        await _postgres.DisposeAsync();
        _timeout.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private async Task QueueRuns(int count)
    {
        await using var scope = _app!.Services.CreateAsyncScope();
        var start = scope.ServiceProvider.GetRequiredService<IUseCase<StartRunInput, StartRunOutput>>();
        for (var index = 0; index < count; index++)
            (await start.Handle(new(_pipelineId, 1, "test"), _timeout.Token)).IsError.ShouldBeFalse();
    }

    private async Task<RunRecord[]> WaitForRuns(int count)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(_timeout.Token);
        limit.CancelAfter(TimeSpan.FromSeconds(5));
        while (true)
        {
            await using var scope = _app!.Services.CreateAsyncScope();
            var runs = await scope.ServiceProvider.GetRequiredService<IDesignerQueries>().GetRuns(0, 25, _pipelineId, limit.Token);
            if (runs.Length == count && runs.All(run => run.Status == "Failed")) return runs;
            await Task.Delay(TimeSpan.FromMilliseconds(25), limit.Token);
        }
    }

    [TestMethod]
    public async Task Execute_When_QueuedAfterIdle_Then_StartsPromptlyAndDrainsWithoutSchedulingGaps()
    {
        await StartApp("Development");
        _worker.IsRunning.ShouldBeTrue();
        var schedulerOnly = new WorkerHealthCheck(_app!.Services.GetRequiredService<ITickerHost>(), new PipelineWorkerState());
        (await schedulerOnly.CheckHealthAsync(new(), _timeout.Token)).Status.ShouldBe(HealthStatus.Unhealthy);
        var definition = new DefinitionRecord(1, [new CsvRecord("csv", "in.csv", [new("age", ColumnTypeRecord.Int64)]), new ParquetRecord("out", "existing.parquet")]);
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var save = scope.ServiceProvider.GetRequiredService<IUseCase<SavePipelineInput, SavePipelineOutput>>();
            (await save.Handle(new(_pipelineId, 0, new("Worker regression", definition, JsonSerializer.SerializeToElement(new { }), new() { ["test"] = new() })), _timeout.Token)).IsError.ShouldBeFalse();
        }
        await Task.Delay(TimeSpan.FromSeconds(3), _timeout.Token);
        await QueueRuns(3);

        var runs = await WaitForRuns(3);

        runs.ShouldAllBe(run => run.StartedAt.HasValue && (run.StartedAt.Value - run.CreatedAt).TotalSeconds < 2);
        (runs.Max(run => run.StartedAt)!.Value - runs.Min(run => run.StartedAt)!.Value).TotalSeconds.ShouldBeLessThan(1);
        runs.ShouldAllBe(run => run.Error == "Output already exists; choose a different filename.");
        await _sut.Execute(_timeout.Token); // A second dashboard invocation must not create another pump.
        _worker.IsRunning.ShouldBeTrue();
        await Task.Delay(TimeSpan.FromSeconds(2), _timeout.Token);
        await QueueRuns(1);
        var afterIdle = (await WaitForRuns(4)).OrderByDescending(run => run.CreatedAt).First();
        (afterIdle.StartedAt!.Value - afterIdle.CreatedAt).TotalSeconds.ShouldBeLessThan(2);
        (await File.ReadAllTextAsync(Path.Combine(_root, "output/existing.parquet"), _timeout.Token)).ShouldBe("preserve me");
        await _app.StopAsync(_timeout.Token);
        using var stopLimit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (_worker.IsRunning) await Task.Delay(TimeSpan.FromMilliseconds(10), stopLimit.Token);
    }

    [TestMethod]
    public async Task Schedules_When_PipelineAttached_Then_RegistersCronTickerQueuesScheduledRunsAndRestoresAfterRestart()
    {
        await StartApp("Development");
        var scheduleId = Guid.NewGuid();
        var alias = "every-minute-" + scheduleId.ToString("N")[..8];
        var definition = new DefinitionRecord(1, [new CsvRecord("csv", "in.csv", [new("age", ColumnTypeRecord.Int64)]), new ParquetRecord("out", "existing.parquet")]);
        await using (var scope = _app!.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            (await services.GetRequiredService<IUseCase<SavePipelineInput, SavePipelineOutput>>().Handle(new(_pipelineId, 0,
                new("Scheduled", definition, JsonSerializer.SerializeToElement(new { }), new() { ["test"] = new() })), _timeout.Token)).IsError.ShouldBeFalse();
            (await services.GetRequiredService<IUseCase<SaveScheduleInput, SaveScheduleOutput>>().Handle(new(scheduleId, 0, alias, "* * * * *", true), _timeout.Token)).IsError.ShouldBeFalse();
            CronTickers(alias).ShouldBeEmpty(); // no pipelines yet: nothing to schedule
            (await services.GetRequiredService<IUseCase<AttachPipelineToScheduleInput, AttachPipelineToScheduleOutput>>().Handle(new(alias.ToUpperInvariant(), _pipelineId, "test"), _timeout.Token)).IsError.ShouldBeFalse();
        }
        var ticker = CronTickers(alias).ShouldHaveSingleItem();
        ticker.Function.ShouldBe(TriggerScheduleJob.FunctionName);
        ticker.Expression.ShouldBe("* * * * *");
        TickerHelper.ReadTickerRequest<Guid>(ticker.Request).ShouldBe(scheduleId);

        await using (var scope = _app.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<IUseCase<TriggerScheduledRunsInput, TriggerScheduledRunsOutput>>().Handle(new(scheduleId), _timeout.Token)).Value.Queued.ShouldBe(1);
        var run = (await WaitForRuns(1)).Single();
        run.Trigger.ShouldBe($"Scheduled: {alias}");
        run.Error.ShouldBe("Output already exists; choose a different filename.");

        await using (var scope = _app.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<IUseCase<SaveScheduleInput, SaveScheduleOutput>>().Handle(new(scheduleId, 1, alias, "*/5 * * * *", false), _timeout.Token)).IsError.ShouldBeFalse();
        CronTickers(alias).ShouldBeEmpty();
        await using (var scope = _app.Services.CreateAsyncScope())
            (await scope.ServiceProvider.GetRequiredService<IUseCase<SaveScheduleInput, SaveScheduleOutput>>().Handle(new(scheduleId, 2, alias, "*/5 * * * *", true), _timeout.Token)).IsError.ShouldBeFalse();
        CronTickers(alias).ShouldHaveSingleItem().Expression.ShouldBe("*/5 * * * *");

        // TickerQ storage is static in-process memory; clear it to simulate a real process restart.
        var storage = _app.Services.GetRequiredService<ITickerPersistenceProvider<TimeTicker, CronTicker>>();
        await storage.RemoveCronTickers(CronTickers(alias), cancellationToken: _timeout.Token);
        await _app.DisposeAsync();
        _client.Dispose();
        await StartApp("Development");

        CronTickers(alias).ShouldHaveSingleItem().Expression.ShouldBe("*/5 * * * *");
    }

    private CronTicker[] CronTickers(string alias) => _app!.Services.GetRequiredService<ITickerPersistenceProvider<TimeTicker, CronTicker>>()
        .GetAllCronTickers(cancellationToken: _timeout.Token).GetAwaiter().GetResult().Where(t => t.Description?.Contains($"'{alias}'") == true).ToArray();

    [TestMethod]
    public async Task Dashboard_When_Development_Then_ServesUiAndNumericStatuses()
    {
        await StartApp("Development");

        var html = await _client.GetStringAsync("/tickerq-dashboard", _timeout.Token);
        var json = await _client.GetStringAsync("/tickerq-dashboard/api/time-tickers", _timeout.Token);

        html.ShouldContain("TickerQConfig");
        html.ShouldContain("/tickerq-dashboard/");
        using var result = JsonDocument.Parse(json);
        result.RootElement.EnumerateArray().ShouldContain(ticker => ticker.GetProperty("function").GetString() == "ExecutePipeline" && ticker.GetProperty("status").ValueKind == JsonValueKind.Number);
    }

    [TestMethod]
    public async Task Dashboard_When_Production_Then_DoesNotExposeUiOrControllers()
    {
        await StartApp("Production");

        using var dashboard = await _client.GetAsync("/tickerq-dashboard", _timeout.Token);
        using var dashboardApi = await _client.GetAsync("/api/options", _timeout.Token);

        dashboard.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        dashboardApi.StatusCode.ShouldBe(System.Net.HttpStatusCode.NotFound);
        _worker.IsRunning.ShouldBeTrue();
    }
}
