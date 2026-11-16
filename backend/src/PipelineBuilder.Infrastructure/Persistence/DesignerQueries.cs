using Microsoft.EntityFrameworkCore;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Infrastructure.Persistence;

public sealed class DesignerQueries(PipelineDbContext db) : IDesignerQueries
{
    public async Task<SavedPipelineRecord?> GetPipeline(Guid id, CancellationToken ct) => (await db.Pipelines.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && !p.IsDeleted, ct))?.MapTo();

    public async Task<PipelineSummaryRecord[]> GetPipelines(int skip, int take, CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<PipelineSummaryRow>("""
            SELECT p."Id", p."Revision", p."Document" ->> 'name' AS "Name",
                ARRAY(SELECT jsonb_object_keys(p."Document" -> 'environments') ORDER BY 1) AS "Environments"
            FROM "Pipelines" p WHERE NOT p."IsDeleted"
            """).OrderBy(p => p.Name).ThenBy(p => p.Id).Skip(skip).Take(take).ToArrayAsync(ct);
        return rows.Select(p => new PipelineSummaryRecord(p.Id, p.Name, p.Revision, p.Environments)).ToArray();
    }
    public Task<int> CountPipelines(CancellationToken ct) => db.Pipelines.CountAsync(p => !p.IsDeleted, ct);
    public async Task<RunRecord?> GetRun(Guid id, CancellationToken ct) => (await db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct))?.MapTo();

    public Task<RunRecord[]> GetRuns(int skip, int take, Guid? pipelineId, CancellationToken ct) => db.Runs.AsNoTracking()
        .Where(r => !pipelineId.HasValue || r.PipelineId == pipelineId.Value)
        .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Skip(skip).Take(take)
        .Select(r => new RunRecord(r.Id, r.PipelineId, r.Revision, r.Environment, r.Status, r.CreatedAt,
            r.StartedAt, r.FinishedAt, r.RowsRead, r.RowsWritten, r.Error, r.BlockId, r.Trigger)).ToArrayAsync(ct);

    public Task<int> CountRuns(Guid? pipelineId, CancellationToken ct) => db.Runs.AsNoTracking()
        .CountAsync(r => !pipelineId.HasValue || r.PipelineId == pipelineId.Value, ct);

    public Task<bool> HasActiveRun(Guid pipelineId, string environment, CancellationToken ct) =>
        db.Runs.AsNoTracking().AnyAsync(r => r.PipelineId == pipelineId && r.Environment == environment && (r.Status == "Queued" || r.Status == "Running"), ct);

    public Task<bool> PipelineNameExists(string name, CancellationToken ct) => db.Database.SqlQuery<bool>($"""
        SELECT EXISTS (SELECT 1 FROM "Pipelines" WHERE NOT "IsDeleted" AND lower("Document" ->> 'name') = lower({name})) AS "Value"
        """).SingleAsync(ct);

    private sealed record PipelineSummaryRow(Guid Id, string Name, int Revision, string[] Environments);
}
