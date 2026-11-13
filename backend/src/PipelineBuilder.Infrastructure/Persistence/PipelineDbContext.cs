using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PipelineBuilder.Application.Contracts;

namespace PipelineBuilder.Infrastructure.Persistence;

public sealed class PipelineDbContext(DbContextOptions<PipelineDbContext> options) : DbContext(options)
{
    internal DbSet<PipelineEntity> Pipelines => Set<PipelineEntity>();
    internal DbSet<RunEntity> Runs => Set<RunEntity>();
    internal DbSet<ScheduleEntity> Schedules => Set<ScheduleEntity>();
    internal DbSet<ScheduledPipelineEntity> ScheduledPipelines => Set<ScheduledPipelineEntity>();

    public async Task Initialize(CancellationToken ct)
    {
        await Database.EnsureCreatedAsync(ct);
        // shortcut: idempotent additive POC schema upgrades mirroring the EF model; adopt EF migrations before further deployed schema changes.
        await Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Pipelines" ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT FALSE;
            ALTER TABLE "Runs" ADD COLUMN IF NOT EXISTS "Trigger" text NOT NULL DEFAULT 'Manual';
            CREATE TABLE IF NOT EXISTS "Schedules" (
                "Id" uuid NOT NULL CONSTRAINT "PK_Schedules" PRIMARY KEY,
                "Alias" text NOT NULL,
                "NormalizedAlias" text NOT NULL,
                "CronExpression" text NOT NULL,
                "IsEnabled" boolean NOT NULL,
                "Revision" integer NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Schedules_NormalizedAlias" ON "Schedules" ("NormalizedAlias");
            CREATE TABLE IF NOT EXISTS "ScheduledPipelines" (
                "ScheduleId" uuid NOT NULL,
                "PipelineId" uuid NOT NULL,
                "Environment" text NOT NULL,
                CONSTRAINT "PK_ScheduledPipelines" PRIMARY KEY ("ScheduleId", "PipelineId", "Environment"),
                CONSTRAINT "FK_ScheduledPipelines_Schedules_ScheduleId" FOREIGN KEY ("ScheduleId") REFERENCES "Schedules" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_ScheduledPipelines_Pipelines_PipelineId" FOREIGN KEY ("PipelineId") REFERENCES "Pipelines" ("Id") ON DELETE RESTRICT);
            CREATE INDEX IF NOT EXISTS "IX_ScheduledPipelines_PipelineId" ON "ScheduledPipelines" ("PipelineId");
            DO $$
            BEGIN
                -- Earlier versions allowed one environment per (schedule, pipeline); widen the key, keeping existing rows.
                IF NOT EXISTS (
                    SELECT 1 FROM pg_index i JOIN pg_attribute a ON a.attrelid = i.indrelid AND a.attnum = ANY (i.indkey)
                    WHERE i.indrelid = '"ScheduledPipelines"'::regclass AND i.indisprimary AND a.attname = 'Environment') THEN
                    ALTER TABLE "ScheduledPipelines" DROP CONSTRAINT "PK_ScheduledPipelines";
                    ALTER TABLE "ScheduledPipelines" ADD CONSTRAINT "PK_ScheduledPipelines" PRIMARY KEY ("ScheduleId", "PipelineId", "Environment");
                END IF;
            END $$;
            """, ct);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var pipeline = modelBuilder.Entity<PipelineEntity>();
        pipeline.ToTable("Pipelines");
        pipeline.HasKey(p => p.Id);
        pipeline.Property(p => p.Document).HasColumnType("jsonb");
        var run = modelBuilder.Entity<RunEntity>();
        run.ToTable("Runs");
        run.HasKey(r => r.Id);
        run.Property(r => r.Snapshot).HasColumnType("jsonb");
        run.HasIndex(r => new { r.Status, r.CreatedAt });
        run.HasOne<PipelineEntity>().WithMany().HasForeignKey(r => r.PipelineId).OnDelete(DeleteBehavior.Restrict);
        var schedule = modelBuilder.Entity<ScheduleEntity>();
        schedule.ToTable("Schedules");
        schedule.HasKey(s => s.Id);
        schedule.HasIndex(s => s.NormalizedAlias).IsUnique();
        var link = modelBuilder.Entity<ScheduledPipelineEntity>();
        link.ToTable("ScheduledPipelines");
        link.HasKey(l => new { l.ScheduleId, l.PipelineId, l.Environment });
        link.HasIndex(l => l.PipelineId);
        link.HasOne<ScheduleEntity>().WithMany().HasForeignKey(l => l.ScheduleId).OnDelete(DeleteBehavior.Restrict);
        link.HasOne<PipelineEntity>().WithMany().HasForeignKey(l => l.PipelineId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PipelineEntity
{
    public Guid Id { get; set; }
    public int Revision { get; set; }
    public bool IsDeleted { get; set; }
    public string Document { get; set; } = "";
    public SavedPipelineRecord MapTo() => new(Id, Revision, JsonSerializer.Deserialize<DocumentRecord>(Document, StoredJson.Options)!);
}

internal sealed class RunEntity
{
    public Guid Id { get; set; }
    public Guid PipelineId { get; set; }
    public int Revision { get; set; }
    public string Environment { get; set; } = "";
    public string Snapshot { get; set; } = "";
    public string Status { get; set; } = "Queued";
    public string Trigger { get; set; } = RunTriggers.Manual;
    public bool CancellationRequested { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long RowsRead { get; set; }
    public long RowsWritten { get; set; }
    public string? Error { get; set; }
    public string? BlockId { get; set; }
    public RunRecord MapTo() => new(Id, PipelineId, Revision, Environment, Status, CreatedAt, StartedAt, FinishedAt, RowsRead, RowsWritten, Error, BlockId, Trigger);
}

internal sealed class ScheduleEntity
{
    public Guid Id { get; set; }
    public string Alias { get; set; } = "";
    public string NormalizedAlias { get; set; } = "";
    public string CronExpression { get; set; } = "";
    public bool IsEnabled { get; set; }
    public int Revision { get; set; }
    public static string Normalize(string alias) => alias.Trim().ToUpperInvariant();
}

internal sealed class ScheduledPipelineEntity
{
    public Guid ScheduleId { get; set; }
    public Guid PipelineId { get; set; }
    public string Environment { get; set; } = "";
}

internal static class StoredJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
        AllowOutOfOrderMetadataProperties = true
    };
}
