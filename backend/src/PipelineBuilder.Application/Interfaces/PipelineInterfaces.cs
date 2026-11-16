using ErrorOr;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Application.Interfaces;

public interface IPipelineRepository
{
    Task<ErrorOr<SavedPipelineRecord>> Save(Guid id, int expectedRevision, DocumentRecord document, CancellationToken ct);
    Task<ErrorOr<bool>> Delete(Guid id, int expectedRevision, CancellationToken ct);
    Task<ErrorOr<RunRecord>> Enqueue(Guid pipelineId, int revision, RunSnapshotRecord snapshot, CancellationToken ct);
    Task<RunRecord> RecordRejected(Guid pipelineId, int revision, string environment, string trigger, string error, CancellationToken ct);
    Task<ClaimedRunRecord?> ClaimNext(CancellationToken ct);
    Task Complete(Guid id, string status, ExecutionResultRecord? result, string? error, string? blockId, CancellationToken ct);
    Task<bool> IsCancellationRequested(Guid id, CancellationToken ct);
    Task<bool> Cancel(Guid id, CancellationToken ct);
}

public sealed record ClaimedRunRecord(Guid Id, RunSnapshotRecord Snapshot);

public interface IDesignerQueries
{
    Task<SavedPipelineRecord?> GetPipeline(Guid id, CancellationToken ct);
    Task<PipelineSummaryRecord[]> GetPipelines(int skip, int take, CancellationToken ct);
    Task<int> CountPipelines(CancellationToken ct);
    Task<RunRecord?> GetRun(Guid id, CancellationToken ct);
    Task<RunRecord[]> GetRuns(int skip, int take, Guid? pipelineId, CancellationToken ct);
    Task<int> CountRuns(Guid? pipelineId, CancellationToken ct);
    Task<bool> HasActiveRun(Guid pipelineId, string environment, CancellationToken ct);
    /// <summary>Case-insensitive match against the names of pipelines that are not deleted.</summary>
    Task<bool> PipelineNameExists(string name, CancellationToken ct);
}

public interface IPipelineFiles
{
    Task<ColumnRecord[]> Discover(string path, string delimiter, bool hasHeader, CancellationToken ct);
    Task<ColumnRecord[]> DiscoverParquet(string path, CancellationToken ct);
    Task<ColumnRecord[]> DiscoverExcel(string path, string sheet, bool hasHeader, CancellationToken ct);
    Task<ApiSchemaDiscoveryRecord> DiscoverApi(ApiInputStep api, CancellationToken ct);
    Task<ColumnRecord[]> DiscoverPostgres(PostgresInputStep postgres, CancellationToken ct);
    Task<ExecutionResultRecord> Execute(DefinitionRecord definition, Func<CancellationToken, Task<bool>> isCancelled, CancellationToken ct);
}

public sealed record ApiSchemaDiscoveryRecord(ColumnRecord[] Schema, string[] DataPaths);

public sealed class PipelineDataException(string message, string? blockId = null, Exception? inner = null) : Exception(message, inner)
{
    public string? BlockId { get; } = blockId;
}
