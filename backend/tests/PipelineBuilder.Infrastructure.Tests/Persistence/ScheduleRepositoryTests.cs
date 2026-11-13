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
public sealed class ScheduleRepositoryTests
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private ScheduleRepository _sut = null!;
    private PipelineDbContext _db = null!;
    private readonly CancellationToken _ct = new CancellationTokenSource().Token;

    [TestInitialize]
    public async Task Initialize()
    {
        await _postgres.StartAsync(_ct);
        _db = new(new DbContextOptionsBuilder<PipelineDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);
        await _db.Initialize(_ct);
        CreateSut();
    }
    private void CreateSut() => _sut = new(_db);

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_db is not null) await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<T> InTransaction<T>(Func<Task<T>> action)
    {
        await using var transaction = await new UnitOfWork(_db).Begin(_ct);
        var result = await action();
        await transaction.Commit(_ct);
        return result;
    }

    private async Task<Guid> CreatePipeline(string name)
    {
        var document = new DocumentRecord(name, new(1, [new CsvRecord("csv", "in.csv", [new("age", ColumnTypeRecord.Int64)]), new ParquetRecord("out", "out.parquet")]),
            JsonSerializer.SerializeToElement(new { }), new() { ["test"] = new() });
        var saved = await InTransaction(() => new PipelineRepository(_db).Save(Guid.NewGuid(), 0, document, _ct));
        return saved.Value.Id;
    }

    [TestMethod]
    public async Task Save_When_AliasDiffersOnlyByCase_Then_RejectsDuplicate()
    {
        (await InTransaction(() => _sut.Save(Guid.NewGuid(), 0, "Nightly", "0 2 * * *", true, _ct))).Value.ShouldBe(1);

        var duplicate = await InTransaction(() => _sut.Save(Guid.NewGuid(), 0, "NIGHTLY", "0 3 * * *", true, _ct));

        duplicate.FirstError.Code.ShouldBe("Schedule.Alias");
        (await new ScheduleQueries(_db).GetSchedules(_ct)).ShouldHaveSingleItem().CronExpression.ShouldBe("0 2 * * *");
    }

    [TestMethod]
    public async Task Save_When_RevisionStale_Then_KeepsSavedSchedule()
    {
        var id = Guid.NewGuid();
        await InTransaction(() => _sut.Save(id, 0, "hourly", "0 * * * *", true, _ct));

        var stale = await InTransaction(() => _sut.Save(id, 0, "hourly", "30 * * * *", false, _ct));

        stale.FirstError.Code.ShouldBe("Schedule.Revision");
        (await new ScheduleQueries(_db).GetSchedule(id, _ct))!.CronExpression.ShouldBe("0 * * * *");
    }

    [TestMethod]
    public async Task Attach_When_PipelinesAdded_Then_QueriesByAliasWithNamesAndEnvironments()
    {
        var id = Guid.NewGuid();
        await InTransaction(() => _sut.Save(id, 0, "Daily", "0 8 * * *", true, _ct));
        var orders = await CreatePipeline("Orders");
        var customers = await CreatePipeline("Customers");
        await InTransaction(async () => { await _sut.Attach(id, orders, "test", _ct); await _sut.Attach(id, customers, "test", _ct); return true; });
        await InTransaction(async () => { await _sut.Attach(id, orders, "test", _ct); return true; }); // re-attaching is idempotent

        var schedule = await new ScheduleQueries(_db).GetScheduleByAlias("dAiLy", _ct);

        schedule!.IsActive.ShouldBeTrue();
        schedule.Pipelines.Select(p => p.PipelineName).ShouldBe(["Customers", "Orders"]);
        schedule.Pipelines.ShouldAllBe(p => p.Environment == "test");
    }

    [TestMethod]
    public async Task Delete_When_PipelinesAttached_Then_BlocksUntilDetached()
    {
        var id = Guid.NewGuid();
        await InTransaction(() => _sut.Save(id, 0, "weekly", "0 8 * * 1", true, _ct));
        var pipeline = await CreatePipeline("Customers");
        await InTransaction(async () => { await _sut.Attach(id, pipeline, "test", _ct); return true; });

        var blocked = await InTransaction(() => _sut.Delete(id, 1, _ct));
        var detached = await InTransaction(() => _sut.Detach(id, pipeline, "test", _ct));
        var deleted = await InTransaction(() => _sut.Delete(id, 1, _ct));

        blocked.FirstError.Code.ShouldBe("Schedule.HasPipelines");
        detached.ShouldBeTrue();
        deleted.Value.ShouldBeTrue();
        (await new ScheduleQueries(_db).GetSchedule(id, _ct)).ShouldBeNull();
    }

    [TestMethod]
    public async Task Attach_When_SamePipelineInSeveralEnvironments_Then_KeepsEachAndDetachesIndividually()
    {
        var id = Guid.NewGuid();
        await InTransaction(() => _sut.Save(id, 0, "schedule-a", "0 8 * * *", true, _ct));
        var job = await CreatePipeline("Job 1");
        await InTransaction(async () => { await _sut.Attach(id, job, "test", _ct); await _sut.Attach(id, job, "dev", _ct); await _sut.Attach(id, job, "dev", _ct); return true; });

        var both = (await new ScheduleQueries(_db).GetSchedule(id, _ct))!.Pipelines;
        var removedDev = await InTransaction(() => _sut.Detach(id, job, "dev", _ct));
        var removedMissing = await InTransaction(() => _sut.Detach(id, job, "prod", _ct));

        both.Select(p => (p.PipelineName, p.Environment)).ShouldBe([("Job 1", "dev"), ("Job 1", "test")]);
        removedDev.ShouldBeTrue();
        removedMissing.ShouldBeFalse();
        (await new ScheduleQueries(_db).GetSchedule(id, _ct))!.Pipelines.ShouldHaveSingleItem().Environment.ShouldBe("test");
    }

    [TestMethod]
    public async Task Initialize_When_AssociationKeyLacksEnvironment_Then_WidensKeyKeepingRows()
    {
        var id = Guid.NewGuid();
        await InTransaction(() => _sut.Save(id, 0, "legacy", "0 8 * * *", true, _ct));
        var job = await CreatePipeline("Job 1");
        await InTransaction(async () => { await _sut.Attach(id, job, "test", _ct); return true; });
        await _db.Database.ExecuteSqlRawAsync("ALTER TABLE \"ScheduledPipelines\" DROP CONSTRAINT \"PK_ScheduledPipelines\"; ALTER TABLE \"ScheduledPipelines\" ADD CONSTRAINT \"PK_ScheduledPipelines\" PRIMARY KEY (\"ScheduleId\", \"PipelineId\");", _ct);

        await _db.Initialize(_ct);
        await _db.Initialize(_ct);
        await InTransaction(async () => { await _sut.Attach(id, job, "dev", _ct); return true; });

        (await new ScheduleQueries(_db).GetSchedule(id, _ct))!.Pipelines.Select(p => p.Environment).ShouldBe(["dev", "test"]);
    }

    [TestMethod]
    public async Task PipelineDelete_When_PipelineScheduled_Then_RemovesAssociationAndDeactivatesSchedule()
    {
        var id = Guid.NewGuid();
        await InTransaction(() => _sut.Save(id, 0, "daily", "0 8 * * *", true, _ct));
        var pipeline = await CreatePipeline("Customers");
        await InTransaction(async () => { await _sut.Attach(id, pipeline, "test", _ct); return true; });

        await InTransaction(() => new PipelineRepository(_db).Delete(pipeline, 1, _ct));

        var schedule = (await new ScheduleQueries(_db).GetSchedule(id, _ct))!;
        schedule.Pipelines.ShouldBeEmpty();
        schedule.IsActive.ShouldBeFalse();
    }

    [TestMethod]
    public async Task RecordRejected_When_ScheduledAttemptFails_Then_AppearsInHistoryWithTrigger()
    {
        var pipeline = await CreatePipeline("Customers");

        await InTransaction(() => new PipelineRepository(_db).RecordRejected(pipeline, 1, "test", "Scheduled: daily", "Missing environment variable 'INPUT'.", _ct));

        var run = (await new DesignerQueries(_db).GetRuns(0, 25, pipeline, _ct)).Single();
        run.Status.ShouldBe("Failed");
        run.Trigger.ShouldBe("Scheduled: daily");
        run.Error.ShouldBe("Missing environment variable 'INPUT'.");
        (await new DesignerQueries(_db).HasActiveRun(pipeline, "test", _ct)).ShouldBeFalse();
    }

    [TestMethod]
    public async Task Initialize_When_UpgradingDatabaseWithoutScheduleTables_Then_AddsThemIdempotently()
    {
        await _db.Database.ExecuteSqlRawAsync("DROP TABLE \"ScheduledPipelines\"; DROP TABLE \"Schedules\"; ALTER TABLE \"Runs\" DROP COLUMN \"Trigger\";", _ct);

        await _db.Initialize(_ct);
        await _db.Initialize(_ct);

        (await InTransaction(() => _sut.Save(Guid.NewGuid(), 0, "after-upgrade", "0 8 * * *", true, _ct))).IsError.ShouldBeFalse();
    }
}
