using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.UseCases.Runs;
using PipelineBuilder.Infrastructure.Files;
using PipelineBuilder.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.PostgreSql;
using Polly;

namespace PipelineBuilder.Infrastructure.Tests.Persistence;

[TestClass]
[TestCategory("Docker")]
public sealed class DesignerQueriesTests
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private DesignerQueries _sut = null!;
    private PipelineDbContext _db = null!;
    private readonly CancellationToken _ct = new CancellationTokenSource().Token;

    [TestInitialize]
    public async Task Initialize()
    {
        await _postgres.StartAsync(_ct);
        _db = new(new DbContextOptionsBuilder<PipelineDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
        await _db.Database.EnsureCreatedAsync(_ct);
        CreateSut();
    }
    private void CreateSut() => _sut = new(_db);

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_db is not null) await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [TestMethod]
    public async Task GetRuns_When_HistoryHasMixedResults_Then_ReturnsNewestFirstWithSummaryAndReason()
    {
        var repository = new PipelineRepository(_db);
        var pipelineId = Guid.NewGuid();
        var definition = new DefinitionRecord(1, [new CsvRecord("csv", "in.csv", [new("age", ColumnTypeRecord.Int64)]), new ParquetRecord("out", "out.parquet")]);
        var document = new DocumentRecord("History test", definition, JsonSerializer.SerializeToElement(new { }), new() { ["test"] = new() });
        List<Guid> ids = [];
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var transaction = await new UnitOfWork(_db).Begin(_ct))
        {
            await repository.Save(pipelineId, 0, document, _ct);
            for (var i = 0; i < 3; i++)
            {
                var run = await repository.Enqueue(pipelineId, 1, new(definition, "test", new() { ["PRIVATE"] = "not-exposed-in-history" }, "1.0.0"), _ct);
                ids.Add(run.Value.Id);
                var timestamp = createdAt.AddSeconds(i);
                await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Runs\" SET \"CreatedAt\" = {timestamp} WHERE \"Id\" = {run.Value.Id}", _ct);
            }
            await transaction.Commit(_ct);
        }
        var successful = (await repository.ClaimNext(_ct))!;
        await repository.Complete(successful.Id, "Succeeded", new(3, 2), null, null, _ct);
        var failed = (await repository.ClaimNext(_ct))!;
        await repository.Complete(failed.Id, "Failed", null, "Record 2 has an invalid age.", "csv", _ct);

        var firstPage = await _sut.GetRuns(0, 2, null, _ct);
        var secondPage = await _sut.GetRuns(2, 2, null, _ct);

        (await _sut.CountRuns(null, _ct)).ShouldBe(3);
        (await _sut.CountRuns(pipelineId, _ct)).ShouldBe(3);
        (await _sut.CountPipelines(_ct)).ShouldBe(1);
        firstPage.Select(r => r.Id).ShouldBe([ids[2], ids[1]]);
        firstPage[0].Status.ShouldBe("Queued");
        firstPage[1].Status.ShouldBe("Failed");
        firstPage[1].Error.ShouldBe("Record 2 has an invalid age.");
        firstPage[1].BlockId.ShouldBe("csv");
        firstPage[1].FinishedAt.ShouldNotBeNull();
        secondPage.Single().Id.ShouldBe(ids[0]);
        secondPage[0].RowsRead.ShouldBe(3);
        secondPage[0].RowsWritten.ShouldBe(2);
        (await _sut.GetRuns(3, 2, null, _ct)).ShouldBeEmpty();

        await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Runs\" SET \"CreatedAt\" = {createdAt}", _ct);
        var tiedRuns = await _sut.GetRuns(0, 3, null, _ct);
        tiedRuns.Select(r => r.Id.ToString()).ShouldBe(ids.Select(id => id.ToString()).OrderByDescending(id => id, StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public async Task GetRuns_When_OutputAlreadyExists_Then_RecordsFailedAttemptWithoutOverwriting()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "input"));
        Directory.CreateDirectory(Path.Combine(root, "output"));
        try
        {
            using var host = new HostBuilder().UseContentRoot(root).Build();
            var files = new PipelineFiles(new ServerPaths(Options.Create(new PipelineFileOptions { InputRoot = "input", OutputRoot = "output" }), host.Services.GetRequiredService<IHostEnvironment>()), ResiliencePipeline.Empty);
            await File.WriteAllTextAsync(Path.Combine(root, "input/in.csv"), "age\n32\n", _ct);
            var output = Path.Combine(root, "output/existing.parquet");
            await File.WriteAllTextAsync(output, "preserve existing file", _ct);
            var repository = new PipelineRepository(_db);
            var pipelineId = Guid.NewGuid();
            var definition = new DefinitionRecord(1, [new CsvRecord("csv", "in.csv", [new("age", ColumnTypeRecord.Int64)]), new ParquetRecord("out", "existing.parquet")]);
            var document = new DocumentRecord("Failed attempt", definition, JsonSerializer.SerializeToElement(new { }), new() { ["test"] = new() });
            Guid runId;
            await using (var transaction = await new UnitOfWork(_db).Begin(_ct))
            {
                await repository.Save(pipelineId, 0, document, _ct);
                var started = await new StartRunCommand(_sut, repository).Handle(new(pipelineId, 1, "test"), _ct);
                started.IsError.ShouldBeFalse();
                runId = started.Value.Run.Id;
                started.Value.Run.Status.ShouldBe("Queued");
                await transaction.Commit(_ct);
            }

            await new ExecuteNextRunCommand(repository, files, NullLogger<ExecuteNextRunCommand>.Instance).Handle(new(), _ct);

            var logged = (await _sut.GetRuns(0, 25, null, _ct)).Single();
            logged.Id.ShouldBe(runId);
            logged.Status.ShouldBe("Failed");
            logged.Error.ShouldBe("Output already exists; choose a different filename.");
            logged.BlockId.ShouldBe("out");
            logged.FinishedAt.ShouldNotBeNull();
            (await File.ReadAllTextAsync(output, _ct)).ShouldBe("preserve existing file");
            Directory.GetFiles(Path.Combine(root, "output")).ShouldHaveSingleItem();
        }
        finally { Directory.Delete(root, true); }
    }
}
