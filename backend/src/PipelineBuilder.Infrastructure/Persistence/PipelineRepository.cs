using System.Text.Json;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Infrastructure.Persistence;

public sealed class PipelineRepository(PipelineDbContext db) : IPipelineRepository
{
    public async Task<ErrorOr<SavedPipelineRecord>> Save(Guid id, int expectedRevision, DocumentRecord document, CancellationToken ct)
    {
        // A per-pipeline lock also protects the first save, where no row exists yet.
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({id.ToString()}))", ct);
        var entity = await db.Pipelines.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (entity?.IsDeleted == true) return Error.NotFound("Pipeline.NotFound", "Pipeline not found.");
        if ((entity?.Revision ?? 0) != expectedRevision) return Error.Conflict("Pipeline.Revision", "Pipeline changed. Reload before saving.");
        if (entity is null) { entity = new PipelineEntity { Id = id }; db.Pipelines.Add(entity); }
        entity.Revision++;
        entity.Document = JsonSerializer.Serialize(document, StoredJson.Options);
        await db.SaveChangesAsync(ct);
        return entity.MapTo();
    }

    public async Task<ErrorOr<bool>> Delete(Guid id, int expectedRevision, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({id.ToString()}))", ct);
        var entity = await db.Pipelines.SingleOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct);
        if (entity is null) return Error.NotFound("Pipeline.NotFound", "Pipeline not found.");
        if (entity.Revision != expectedRevision) return Error.Conflict("Pipeline.Revision", "Pipeline changed. Refresh the list before deleting.");
        if (await db.Runs.AnyAsync(r => r.PipelineId == id && (r.Status == "Queued" || r.Status == "Running"), ct))
            return Error.Conflict("Pipeline.ActiveRuns", "Wait for queued or running executions to finish before deleting this pipeline.");
        entity.IsDeleted = true;
        entity.Revision++;
        db.ScheduledPipelines.RemoveRange(await db.ScheduledPipelines.Where(l => l.PipelineId == id).ToArrayAsync(ct));
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ErrorOr<RunRecord>> Enqueue(Guid pipelineId, int revision, RunSnapshotRecord snapshot, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({pipelineId.ToString()}))", ct);
        if (!await db.Pipelines.AnyAsync(p => p.Id == pipelineId && p.Revision == revision && !p.IsDeleted, ct))
            return Error.Conflict("Pipeline.Revision", "Pipeline changed before the run was queued.");
        var entity = new RunEntity
        {
            Id = Guid.NewGuid(), PipelineId = pipelineId, Revision = revision, Environment = snapshot.Environment,
            Snapshot = JsonSerializer.Serialize(snapshot, StoredJson.Options), CreatedAt = DateTimeOffset.UtcNow, Trigger = snapshot.Trigger
        };
        db.Runs.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.MapTo();
    }

    public async Task<RunRecord> RecordRejected(Guid pipelineId, int revision, string environment, string trigger, string error, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        // No executable snapshot exists for an attempt rejected before queueing.
        var entity = new RunEntity
        {
            Id = Guid.NewGuid(), PipelineId = pipelineId, Revision = revision, Environment = environment, Snapshot = "{}",
            Status = "Failed", Trigger = trigger, CreatedAt = now, StartedAt = now, FinishedAt = now, Error = error
        };
        db.Runs.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity.MapTo();
    }

    public async Task<ClaimedRunRecord?> ClaimNext(CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var runs = await db.Runs.FromSqlRaw("SELECT * FROM \"Runs\" WHERE \"Status\" = 'Queued' ORDER BY \"CreatedAt\" LIMIT 1 FOR UPDATE SKIP LOCKED").ToArrayAsync(ct);
        var run = runs.FirstOrDefault();
        if (run is null) return null;
        run.Status = "Running";
        run.StartedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(run.Id, JsonSerializer.Deserialize<RunSnapshotRecord>(run.Snapshot, StoredJson.Options)!);
    }

    public async Task Complete(Guid id, string status, ExecutionResultRecord? result, string? error, string? blockId, CancellationToken ct) =>
        await db.Runs.Where(r => r.Id == id && r.Status == "Running").ExecuteUpdateAsync(set => set
            .SetProperty(r => r.Status, status).SetProperty(r => r.FinishedAt, DateTimeOffset.UtcNow)
            .SetProperty(r => r.RowsRead, result == null ? 0 : result.RowsRead).SetProperty(r => r.RowsWritten, result == null ? 0 : result.RowsWritten)
            .SetProperty(r => r.Error, error).SetProperty(r => r.BlockId, blockId), ct);

    public Task<bool> IsCancellationRequested(Guid id, CancellationToken ct) => db.Runs.AsNoTracking().AnyAsync(r => r.Id == id && r.CancellationRequested, ct);

    public async Task<bool> Cancel(Guid id, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.Runs.Where(r => r.Id == id && (r.Status == "Queued" || r.Status == "Running")).ExecuteUpdateAsync(set => set
            .SetProperty(r => r.CancellationRequested, true)
            .SetProperty(r => r.FinishedAt, r => r.Status == "Queued" ? now : r.FinishedAt)
            .SetProperty(r => r.Status, r => r.Status == "Queued" ? "Cancelled" : r.Status), ct) > 0;
    }
}
