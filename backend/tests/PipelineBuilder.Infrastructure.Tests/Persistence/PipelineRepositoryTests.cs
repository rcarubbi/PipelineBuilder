using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Infrastructure.Persistence;
using Shouldly;
using Testcontainers.PostgreSql;

namespace PipelineBuilder.Infrastructure.Tests.Persistence;

[TestClass]
[TestCategory("Docker")]
public sealed class PipelineRepositoryTests
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private PipelineRepository _sut = null!;
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
    public async Task Save_When_RevisionIsStale_Then_PreservesSavedDocument()
    {
        var id = Guid.NewGuid();
        var document = new DocumentRecord("Original", new(1, [new CsvRecord("csv", "in.csv", [new("age", ColumnTypeRecord.Int64)]), new ParquetRecord("out", "out.parquet")]), JsonSerializer.SerializeToElement(new { }), new() { ["test"] = new() });
        await using (var transaction = await new UnitOfWork(_db).Begin(_ct))
        {
            (await _sut.Save(id, 0, document, _ct)).IsError.ShouldBeFalse();
            await transaction.Commit(_ct);
        }

        await using var rejectedTransaction = await new UnitOfWork(_db).Begin(_ct);
        var result = await _sut.Save(id, 0, document with { Name = "Overwrite" }, _ct);

        result.FirstError.Type.ShouldBe(ErrorOr.ErrorType.Conflict);
        (await new DesignerQueries(_db).GetPipeline(id, _ct))!.Document.Name.ShouldBe("Original");
    }

    [TestMethod]
    public async Task ClaimNext_When_RunWasQueued_Then_RestoresImmutableSnapshotAndClaimsOnlyOnce()
    {
        var id = Guid.NewGuid();
        var definition = new DefinitionRecord(1, [new CsvRecord("csv", "in.csv", [new("age", ColumnTypeRecord.Int64)]), new ParquetRecord("out", "out.parquet")]);
        var document = new DocumentRecord("Example", definition, JsonSerializer.SerializeToElement(new { }), new() { ["test"] = new() });
        await using (var transaction = await new UnitOfWork(_db).Begin(_ct))
        {
            await _sut.Save(id, 0, document, _ct);
            await _sut.Enqueue(id, 1, new(definition, "test", new(), "1.0.0"), _ct);
            await transaction.Commit(_ct);
        }

        var claimed = await _sut.ClaimNext(_ct);

        claimed.ShouldNotBeNull();
        ((CsvRecord)claimed.Snapshot.Definition.Steps[0]).Path.ShouldBe("in.csv");
        (await _sut.ClaimNext(_ct)).ShouldBeNull();
        (await new DesignerQueries(_db).GetRun(claimed.Id, _ct))!.Status.ShouldBe("Running");
    }

    private async Task<SavedPipelineRecord> CreatePipeline(string name)
    {
        var document = new DocumentRecord(name, new(1, [new CsvRecord("csv", "in.csv", [new("age", ColumnTypeRecord.Int64)]), new ParquetRecord("out", "out.parquet")]),
            JsonSerializer.SerializeToElement(new { }), new() { ["test"] = new() { ["PRIVATE"] = "not exposed in list" }, ["development"] = new() });
        await using var transaction = await new UnitOfWork(_db).Begin(_ct);
        var saved = await _sut.Save(Guid.NewGuid(), 0, document, _ct);
        await transaction.Commit(_ct);
        return saved.Value;
    }

    [TestMethod]
    public async Task Delete_When_ExecutionsFinished_Then_HidesPipelineAndPreservesHistory()
    {
        var pipeline = await CreatePipeline("Deleted pipeline");
        await using (var transaction = await new UnitOfWork(_db).Begin(_ct))
        {
            await _sut.Enqueue(pipeline.Id, 1, new(pipeline.Document.Definition, "test", new(), "1.0.0"), _ct);
            await transaction.Commit(_ct);
        }
        var claimed = (await _sut.ClaimNext(_ct))!;
        await _sut.Complete(claimed.Id, "Failed", null, "Output exists.", "out", _ct);

        await using (var transaction = await new UnitOfWork(_db).Begin(_ct))
        {
            (await _sut.Delete(pipeline.Id, 1, _ct)).Value.ShouldBeTrue();
            await transaction.Commit(_ct);
        }

        var queries = new DesignerQueries(_db);
        (await queries.GetPipeline(pipeline.Id, _ct)).ShouldBeNull();
        (await queries.GetPipelines(0, 25, _ct)).ShouldBeEmpty();
        (await queries.GetRuns(0, 25, pipeline.Id, _ct)).Single().Error.ShouldBe("Output exists.");
        await using var rejectedTransaction = await new UnitOfWork(_db).Begin(_ct);
        (await _sut.Save(pipeline.Id, 0, pipeline.Document, _ct)).FirstError.Type.ShouldBe(ErrorOr.ErrorType.NotFound);
        (await _sut.Enqueue(pipeline.Id, 1, new(pipeline.Document.Definition, "test", new(), "1.0.0"), _ct)).IsError.ShouldBeTrue();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Delete_When_RunIsQueuedOrRunning_Then_RejectsDeletion(bool running)
    {
        var pipeline = await CreatePipeline("Active pipeline");
        await using (var transaction = await new UnitOfWork(_db).Begin(_ct))
        {
            await _sut.Enqueue(pipeline.Id, 1, new(pipeline.Document.Definition, "test", new(), "1.0.0"), _ct);
            await transaction.Commit(_ct);
        }
        if (running) await _sut.ClaimNext(_ct);
        await using var rejectedTransaction = await new UnitOfWork(_db).Begin(_ct);

        var result = await _sut.Delete(pipeline.Id, 1, _ct);

        result.FirstError.Code.ShouldBe("Pipeline.ActiveRuns");
        (await new DesignerQueries(_db).GetPipeline(pipeline.Id, _ct)).ShouldNotBeNull();
    }

    [TestMethod]
    public async Task Delete_When_RevisionIsStale_Then_RejectsDeletion()
    {
        var pipeline = await CreatePipeline("Unchanged pipeline");
        await using var transaction = await new UnitOfWork(_db).Begin(_ct);

        var result = await _sut.Delete(pipeline.Id, 2, _ct);

        result.FirstError.Code.ShouldBe("Pipeline.Revision");
        (await new DesignerQueries(_db).GetPipeline(pipeline.Id, _ct)).ShouldNotBeNull();
    }

    [TestMethod]
    public async Task GetPipelines_When_SavedPipelinesExist_Then_ReturnsPagedMetadataAndFilteredLogs()
    {
        var beta = await CreatePipeline("Beta");
        var alpha = await CreatePipeline("Alpha");
        var queries = new DesignerQueries(_db);
        await using (var transaction = await new UnitOfWork(_db).Begin(_ct))
        {
            await _sut.Enqueue(alpha.Id, 1, new(alpha.Document.Definition, "test", new(), "1.0.0"), _ct);
            await _sut.Enqueue(beta.Id, 1, new(beta.Document.Definition, "test", new(), "1.0.0"), _ct);
            await transaction.Commit(_ct);
        }

        var firstPage = await queries.GetPipelines(0, 1, _ct);
        var secondPage = await queries.GetPipelines(1, 1, _ct);

        firstPage.Single().Name.ShouldBe("Alpha");
        firstPage[0].Environments.ShouldBe(["development", "test"]);
        secondPage.Single().Name.ShouldBe("Beta");
        (await queries.GetRuns(0, 25, alpha.Id, _ct)).Single().PipelineId.ShouldBe(alpha.Id);
    }

    [TestMethod]
    public async Task PipelineNameExists_When_NameDiffersByCaseOrPipelineDeleted_Then_MatchesOnlyLivePipelines()
    {
        var pipeline = await CreatePipeline("Customer Export");
        var queries = new DesignerQueries(_db);

        (await queries.PipelineNameExists("customer export", _ct)).ShouldBeTrue();
        (await queries.PipelineNameExists("Customer Export (imported)", _ct)).ShouldBeFalse();
        (await queries.PipelineNameExists("x' OR '1'='1", _ct)).ShouldBeFalse();
        await using (var transaction = await new UnitOfWork(_db).Begin(_ct))
        {
            await _sut.Delete(pipeline.Id, 1, _ct);
            await transaction.Commit(_ct);
        }
        (await queries.PipelineNameExists("Customer Export", _ct)).ShouldBeFalse();
    }

    [TestMethod]
    public async Task Initialize_When_UpgradingExistingDatabase_Then_KeepsSavedDataAndIsIdempotent()
    {
        var pipeline = await CreatePipeline("Existing pipeline");
        await _db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Pipelines\" DROP COLUMN \"IsDeleted\"", _ct);

        await _db.Initialize(_ct);
        await _db.Initialize(_ct);

        (await new DesignerQueries(_db).GetPipeline(pipeline.Id, _ct))!.Document.Name.ShouldBe("Existing pipeline");
    }
}
