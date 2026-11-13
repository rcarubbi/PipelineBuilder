using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Infrastructure.Persistence;

public sealed class ScheduleQueries(PipelineDbContext db) : IScheduleQueries
{
    public Task<ScheduleRecord[]> GetSchedules(CancellationToken ct) => Load(db.Schedules.AsNoTracking(), ct);

    public async Task<ScheduleRecord?> GetSchedule(Guid id, CancellationToken ct) =>
        (await Load(db.Schedules.AsNoTracking().Where(s => s.Id == id), ct)).SingleOrDefault();

    public async Task<ScheduleRecord?> GetScheduleByAlias(string alias, CancellationToken ct)
    {
        var normalized = ScheduleEntity.Normalize(alias);
        return (await Load(db.Schedules.AsNoTracking().Where(s => s.NormalizedAlias == normalized), ct)).SingleOrDefault();
    }

    private async Task<ScheduleRecord[]> Load(IQueryable<ScheduleEntity> query, CancellationToken ct)
    {
        var schedules = await query.OrderBy(s => s.NormalizedAlias).ToArrayAsync(ct);
        var ids = schedules.Select(s => s.Id).ToArray();
        var links = await (from link in db.ScheduledPipelines.AsNoTracking()
                           join pipeline in db.Pipelines.AsNoTracking() on link.PipelineId equals pipeline.Id
                           where ids.Contains(link.ScheduleId) && !pipeline.IsDeleted
                           select new { link.ScheduleId, link.PipelineId, link.Environment, pipeline.Document }).ToArrayAsync(ct);
        var byschedule = links.ToLookup(l => l.ScheduleId, l => new ScheduledPipelineRecord(l.PipelineId, PipelineName(l.Document), l.Environment));
        return schedules.Select(s => new ScheduleRecord(s.Id, s.Alias, s.CronExpression, s.IsEnabled, s.Revision,
            byschedule[s.Id].OrderBy(p => p.PipelineName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Environment, StringComparer.OrdinalIgnoreCase).ToArray())).ToArray();
    }

    private static string PipelineName(string document)
    {
        using var json = JsonDocument.Parse(document);
        return json.RootElement.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "";
    }
}
