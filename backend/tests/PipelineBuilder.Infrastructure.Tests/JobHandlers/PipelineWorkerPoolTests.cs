using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.UseCases.Runs;
using PipelineBuilder.Infrastructure.JobHandlers;
using Shouldly;

namespace PipelineBuilder.Infrastructure.Tests.JobHandlers;

[TestClass]
public sealed class PipelineWorkerPoolTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(65)]
    public void Options_When_PoolSizeIsOutsideRange_Then_ValidationFails(int poolSize)
    {
        var services = new ServiceCollection();
        services.AddOptions<PipelineWorkerOptions>().Configure(options => options.PoolSize = poolSize).ValidateDataAnnotations();
        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<PipelineWorkerOptions>>().Value);
    }

    [TestMethod]
    public async Task Execute_When_PoolIsFull_Then_UsesSeparateScopesAndClaimsNextAfterCompletion()
    {
        var work = new BlockingWork();
        await using var services = new ServiceCollection()
            .AddSingleton(work)
            .AddScoped<IUseCase<ExecuteNextRunInput, ExecuteNextRunOutput>, BlockingUseCase>()
            .BuildServiceProvider();
        var state = new PipelineWorkerState();
        var sut = new ExecutePipelineJob(services.GetRequiredService<IServiceScopeFactory>(), state,
            Options.Create(new PipelineWorkerOptions { PoolSize = 2 }), NullLogger<ExecutePipelineJob>.Instance);
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = sut.Execute(ct.Token);
        try
        {
            await work.TwoStarted.Task.WaitAsync(ct.Token);
            work.Started.ShouldBe(2);
            work.Instances.ShouldBe(2);
            await sut.Execute(ct.Token); // Duplicate ticker must not start another pool.
            work.Started.ShouldBe(2);

            work.Release.Release();
            await work.ThirdStarted.Task.WaitAsync(ct.Token);
            work.Started.ShouldBe(3);
            work.Instances.ShouldBe(3);
            state.IsRunning.ShouldBeTrue();
        }
        finally
        {
            await ct.CancelAsync();
            await Should.ThrowAsync<OperationCanceledException>(() => running);
        }
        state.IsRunning.ShouldBeFalse();
    }

    private sealed class BlockingWork
    {
        public int Started;
        public int Instances;
        public readonly SemaphoreSlim Release = new(0);
        public readonly TaskCompletionSource TwoStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ThirdStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BlockingUseCase : IUseCase<ExecuteNextRunInput, ExecuteNextRunOutput>
    {
        private readonly BlockingWork _work;

        public BlockingUseCase(BlockingWork work)
        {
            _work = work;
            Interlocked.Increment(ref _work.Instances);
        }

        public async Task<ErrorOr<ExecuteNextRunOutput>> Handle(ExecuteNextRunInput input, CancellationToken ct)
        {
            var started = Interlocked.Increment(ref _work.Started);
            if (started == 2) _work.TwoStarted.TrySetResult();
            if (started == 3) _work.ThirdStarted.TrySetResult();
            await _work.Release.WaitAsync(ct);
            return new ExecuteNextRunOutput(true);
        }
    }
}
