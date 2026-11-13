using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;
using TickerQ.Utilities;
using TickerQ.Utilities.Interfaces;
using TickerQ.Utilities.Models.Ticker;

namespace PipelineBuilder.Infrastructure.Tests.JobHandlers;

[TestClass]
public sealed class TickerRegistrationTests
{
    [TestMethod]
    public void AddInfrastructure_When_AnotherAssemblyClearsFunctions_Then_RegistersPipelineWorker()
    {
        TickerFunctionProvider.RegisterFunctions(new Dictionary<string, (string, TickerQ.Utilities.Enums.TickerTaskPriority, TickerFunctionDelegate)>());
        var services = new ServiceCollection();

        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Pipelines"] = "Host=localhost;Database=registration-test"
        }).Build(), new TestEnvironment());

        TickerFunctionProvider.TickerFunctions.ContainsKey("ExecutePipeline").ShouldBeTrue();
        TickerFunctionProvider.TickerFunctions["ExecutePipeline"].Priority.ShouldBe(TickerQ.Utilities.Enums.TickerTaskPriority.LongRunning);
    }

    [TestMethod]
    public async Task GetChildTickersByParentId_When_InMemoryScheduling_Then_DoesNotStopTheWorker()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Pipelines"] = "Host=localhost;Database=registration-test"
        }).Build(), new TestEnvironment());
        await using var provider = services.BuildServiceProvider();
        var storage = provider.GetRequiredService<ITickerPersistenceProvider<TimeTicker, CronTicker>>();
        using var cancellation = new CancellationTokenSource();

        var children = await storage.GetChildTickersByParentId(Guid.NewGuid(), cancellation.Token);

        children.ShouldBeEmpty();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "PipelineBuilder";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
