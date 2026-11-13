using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Infrastructure.Persistence;

public sealed class ScheduleRepository(PipelineDbContext db) : IScheduleRepository
{
    private const string UniqueViolation = "23505";

    public async Task<ErrorOr<int>> Save(Guid id, int expectedRevision, string alias, string cronExpression, bool isEnabled, CancellationToken ct)
    {
        await Lock(id, ct);
        var entity = await db.Schedules.SingleOrDefaultAsync(s => s.Id == id, ct);
        if ((entity?.Revision ?? 0) != expectedRevision) return Error.Conflict("Schedule.Revision", "Schedule changed. Reload before saving.");
        var normalized = ScheduleEntity.Normalize(alias);
        if (await db.Schedules.AnyAsync(s => s.NormalizedAlias == normalized && s.Id != id, ct)) return AliasTaken(alias);
        if (entity is null) { entity = new ScheduleEntity { Id = id }; db.Schedules.Add(entity); }
        entity.Alias = alias;
        entity.NormalizedAlias = normalized;
        entity.CronExpression = cronExpression;
        entity.IsEnabled = isEnabled;
        entity.Revision++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: UniqueViolation }) { return AliasTaken(alias); }
        return entity.Revision;
    }

    public async Task<ErrorOr<bool>> Delete(Guid id, int expectedRevision, CancellationToken ct)
    {
        await Lock(id, ct);
        var entity = await db.Schedules.SingleOrDefaultAsync(s => s.Id == id, ct);
        if (entity is null) return Error.NotFound("Schedule.NotFound", "Schedule not found.");
        if (entity.Revision != expectedRevision) return Error.Conflict("Schedule.Revision", "Schedule changed. Reload before deleting.");
        if (await db.ScheduledPipelines.AnyAsync(l => l.ScheduleId == id, ct))
            return Error.Conflict("Schedule.HasPipelines", "Remove the pipelines attached to this schedule before deleting it.");
        db.Schedules.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Adds the (pipeline, environment) association; adding an existing one is a no-op.</summary>
    public async Task Attach(Guid scheduleId, Guid pipelineId, string environment, CancellationToken ct)
    {
        await Lock(scheduleId, ct);
        if (await db.ScheduledPipelines.AnyAsync(l => l.ScheduleId == scheduleId && l.PipelineId == pipelineId && l.Environment == environment, ct)) return;
        db.ScheduledPipelines.Add(new ScheduledPipelineEntity { ScheduleId = scheduleId, PipelineId = pipelineId, Environment = environment });
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> Detach(Guid scheduleId, Guid pipelineId, string environment, CancellationToken ct)
    {
        await Lock(scheduleId, ct);
        var link = await db.ScheduledPipelines.SingleOrDefaultAsync(l => l.ScheduleId == scheduleId && l.PipelineId == pipelineId && l.Environment == environment, ct);
        if (link is null) return false;
        db.ScheduledPipelines.Remove(link);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private Task Lock(Guid scheduleId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({"schedule:" + scheduleId}))", ct);

    private static Error AliasTaken(string alias) => Error.Conflict("Schedule.Alias", $"A schedule named '{alias}' already exists.");
}
