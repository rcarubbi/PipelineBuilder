using System.Text.Json;
using System.Text.Json.Serialization;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Application.Contracts;

public sealed record ColumnRecord(string Name, ColumnTypeRecord Type, bool Nullable = false, int Precision = 18, int Scale = 2);
public enum ColumnTypeRecord { String, Boolean, Int64, Decimal, Double, Date, Timestamp }
public sealed record MappingRecord(string Source, ColumnRecord Target, string? Format = null);
public sealed record DefinitionRecord(int FormatVersion, StepRecord[] Steps);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CsvRecord), "csv")]
[JsonDerivedType(typeof(MapRecord), "map")]
[JsonDerivedType(typeof(FilterRecord), "filter")]
[JsonDerivedType(typeof(ParquetRecord), "parquet")]
public abstract record StepRecord(string Id);
public sealed record CsvRecord(string Id, string Path, ColumnRecord[] Schema, string Delimiter = ",", bool HasHeader = true) : StepRecord(Id);
public sealed record MapRecord(string Id, MappingRecord[] Mappings) : StepRecord(Id);
public sealed record FilterRecord(string Id, PredicateRecord Criteria) : StepRecord(Id);
public sealed record ParquetRecord(string Id, string Path) : StepRecord(Id);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ComparisonRecord), "compare")]
[JsonDerivedType(typeof(NullRecord), "null")]
[JsonDerivedType(typeof(LogicalRecord), "logical")]
public abstract record PredicateRecord;
public sealed record ComparisonRecord(string Column, string Operator, string Value) : PredicateRecord;
public sealed record NullRecord(string Column, bool IsNull) : PredicateRecord;
public sealed record LogicalRecord(string Operator, PredicateRecord[] Children) : PredicateRecord;
public sealed record IssueRecord(string BlockId, string Message);
public sealed record SchemaRecord(string BlockId, ColumnRecord[] Input, ColumnRecord[] Output);
public sealed record AnalysisRecord(IssueRecord[] Issues, SchemaRecord[] Schemas);
public sealed record DocumentRecord(string Name, DefinitionRecord Definition, JsonElement Workspace, Dictionary<string, Dictionary<string, string>> Environments);
public sealed record SavedPipelineRecord(Guid Id, int Revision, DocumentRecord Document);
public sealed record PipelineSummaryRecord(Guid Id, string Name, int Revision, string[] Environments);
public sealed record RunRecord(Guid Id, Guid PipelineId, int Revision, string Environment, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, long RowsRead, long RowsWritten, string? Error, string? BlockId, string Trigger = RunTriggers.Manual);
public sealed record RunSnapshotRecord(DefinitionRecord Definition, string Environment, Dictionary<string, string> Variables, string EngineVersion, string Trigger = RunTriggers.Manual);
public sealed record ExecutionResultRecord(long RowsRead, long RowsWritten);

public static class RunTriggers
{
    public const string Manual = "Manual";
    public static string Scheduled(string alias) => $"Scheduled: {alias}";
}

public static class DefinitionMapping
{
    public static Column MapTo(this ColumnRecord column) => new(column.Name, (ColumnType)column.Type, column.Nullable, column.Precision, column.Scale);
    public static ColumnRecord MapTo(this Column column) => new(column.Name, (ColumnTypeRecord)column.Type, column.Nullable, column.Precision, column.Scale);
    public static PipelineDefinition MapTo(this DefinitionRecord definition) => new(definition.FormatVersion, definition.Steps.Select(MapTo).ToArray());
    public static PipelineStep MapTo(this StepRecord step) => step switch
    {
        CsvRecord csv => new CsvStep(csv.Id, csv.Path, csv.Schema.Select(MapTo).ToArray(), csv.Delimiter, csv.HasHeader),
        MapRecord map => new MapStep(map.Id, map.Mappings.Select(m => new ColumnMapping(m.Source, m.Target.MapTo(), m.Format)).ToArray()),
        FilterRecord filter => new FilterStep(filter.Id, filter.Criteria.MapTo()),
        ParquetRecord parquet => new ParquetStep(parquet.Id, parquet.Path),
        _ => throw new ArgumentException("Unsupported step.")
    };
    public static IRowPredicate MapTo(this PredicateRecord predicate) => predicate switch
    {
        ComparisonRecord compare => new ComparisonPredicate(compare.Column, Enum.Parse<ComparisonOperator>(compare.Operator, true), compare.Value),
        NullRecord check => new NullPredicate(check.Column, check.IsNull),
        LogicalRecord logical => new LogicalPredicate(Enum.Parse<LogicalOperator>(logical.Operator, true), logical.Children.Select(MapTo).ToArray()),
        _ => throw new ArgumentException("Unsupported expression.")
    };
    public static AnalysisRecord MapTo(this PipelineAnalysis analysis) => new(
        analysis.Issues.Select(i => new IssueRecord(i.BlockId, i.Message)).ToArray(),
        analysis.Schemas.Select(s => new SchemaRecord(s.BlockId, s.Input.Select(MapTo).ToArray(), s.Output.Select(MapTo).ToArray())).ToArray());
}
