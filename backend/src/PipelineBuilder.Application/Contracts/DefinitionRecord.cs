using System.Text.Json;
using System.Text.Json.Serialization;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Application.Contracts;

public sealed record ColumnRecord(string Name, ColumnTypeRecord Type, bool Nullable = false, int Precision = 18, int Scale = 2,
    string? TypeVariable = null, string? NullableVariable = null, string? PrecisionVariable = null, string? ScaleVariable = null);
public enum ColumnTypeRecord { String, Boolean, Int64, Decimal, Double, Date, Timestamp }
public sealed record MappingRecord(string Source, ColumnRecord Target, string? Format = null);
public sealed record DefinitionRecord(int FormatVersion, StepRecord[] Steps, VariableRecord[]? Variables = null);

public sealed record VariableRecord(string Name, TextExpressionRecord Value);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TextLiteralRecord), "literal")]
[JsonDerivedType(typeof(TextConcatRecord), "concat")]
[JsonDerivedType(typeof(TextVariableRecord), "variable")]
[JsonDerivedType(typeof(TextEnvironmentRecord), "environment")]
[JsonDerivedType(typeof(TextUtcRecord), "utc")]
public abstract record TextExpressionRecord(string Id);
public sealed record TextLiteralRecord(string Id, string Value) : TextExpressionRecord(Id);
public sealed record TextConcatRecord(string Id, TextExpressionRecord Left, TextExpressionRecord Right) : TextExpressionRecord(Id);
public sealed record TextVariableRecord(string Id, string Name) : TextExpressionRecord(Id);
public sealed record TextEnvironmentRecord(string Id, string Name) : TextExpressionRecord(Id);
public sealed record TextUtcRecord(string Id, string Format, TextExpressionRecord? FormatExpression = null) : TextExpressionRecord(Id);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CsvRecord), "csv")]
[JsonDerivedType(typeof(MapRecord), "map")]
[JsonDerivedType(typeof(FilterRecord), "filter")]
[JsonDerivedType(typeof(ParquetRecord), "parquet")]
[JsonDerivedType(typeof(CsvOutputRecord), "csvOutput")]
[JsonDerivedType(typeof(ParquetInputRecord), "parquetInput")]
[JsonDerivedType(typeof(ExcelInputRecord), "excelInput")]
[JsonDerivedType(typeof(ExcelOutputRecord), "excelOutput")]
[JsonDerivedType(typeof(ApiInputRecord), "apiInput")]
[JsonDerivedType(typeof(PostgresInputRecord), "postgresInput")]
public abstract record StepRecord(string Id);
public sealed record CsvRecord(string Id, string Path, ColumnRecord[] Schema, string Delimiter = ",", bool HasHeader = true, TextExpressionRecord? PathExpression = null, string? HasHeaderVariable = null) : StepRecord(Id);
public sealed record MapRecord(string Id, MappingRecord[] Mappings) : StepRecord(Id);
public sealed record FilterRecord(string Id, PredicateRecord Criteria) : StepRecord(Id);
public sealed record ParquetRecord(string Id, string Path, TextExpressionRecord? PathExpression = null) : StepRecord(Id);
public sealed record CsvOutputRecord(string Id, string Path, string Delimiter = ",", bool HasHeader = true, TextExpressionRecord? PathExpression = null, string? HasHeaderVariable = null) : StepRecord(Id);
public sealed record ParquetInputRecord(string Id, string Path, ColumnRecord[] Schema, TextExpressionRecord? PathExpression = null) : StepRecord(Id);
public sealed record ExcelInputRecord(string Id, string Path, ColumnRecord[] Schema, string Sheet, bool HasHeader = true, TextExpressionRecord? PathExpression = null, string? HasHeaderVariable = null) : StepRecord(Id);
public sealed record ExcelOutputRecord(string Id, string Path, string Sheet, bool HasHeader = true, TextExpressionRecord? PathExpression = null, string? HasHeaderVariable = null) : StepRecord(Id);
public sealed record ApiInputRecord(string Id, string Url, ColumnRecord[] Schema, string DataPath = "", string Method = "GET",
    string Format = "auto", string Authentication = "none", string? Username = null, string? Password = null,
    string? Token = null, string? ApiKeyName = null, string? ApiKey = null, string? OAuthTokenUrl = null,
    string? ClientId = null, string? ClientSecret = null, string? Scope = null,
    string? NextPath = null, bool UseLinkHeader = false, string? Body = null, Dictionary<string, string>? Headers = null,
    string? UseLinkHeaderVariable = null) : StepRecord(Id);
public sealed record PostgresInputRecord(string Id, string Host, string Port, string Database, string Username, string Password,
    string SslMode, string Query, ColumnRecord[] Schema) : StepRecord(Id);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ComparisonRecord), "compare")]
[JsonDerivedType(typeof(NullRecord), "null")]
[JsonDerivedType(typeof(LogicalRecord), "logical")]
public abstract record PredicateRecord;
public sealed record ComparisonRecord(string Column, string Operator, string Value, TextExpressionRecord? ColumnExpression = null,
    TextExpressionRecord? ValueExpression = null, string? OperatorVariable = null) : PredicateRecord;
public sealed record NullRecord(string Column, bool IsNull, string? IsNullVariable = null) : PredicateRecord;
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
        CsvRecord csv => new CsvStep(csv.Id, csv.Path, csv.Schema.Select(MapTo).ToArray(), csv.Delimiter, csv.HasHeader, csv.HasHeaderVariable),
        MapRecord map => new MapStep(map.Id, map.Mappings.Select(m => new ColumnMapping(m.Source, m.Target.MapTo(), m.Format)).ToArray()),
        FilterRecord filter => new FilterStep(filter.Id, filter.Criteria.MapTo()),
        ParquetRecord parquet => new ParquetStep(parquet.Id, parquet.Path),
        CsvOutputRecord csv => new CsvOutputStep(csv.Id, csv.Path, csv.Delimiter, csv.HasHeader, csv.HasHeaderVariable),
        ParquetInputRecord parquet => new ParquetInputStep(parquet.Id, parquet.Path, parquet.Schema.Select(MapTo).ToArray()),
        ExcelInputRecord excel => new ExcelInputStep(excel.Id, excel.Path, excel.Schema.Select(MapTo).ToArray(), excel.Sheet, excel.HasHeader, excel.HasHeaderVariable),
        ExcelOutputRecord excel => new ExcelOutputStep(excel.Id, excel.Path, excel.Sheet, excel.HasHeader, excel.HasHeaderVariable),
        ApiInputRecord api => new ApiInputStep(api.Id, api.Url, api.Schema.Select(MapTo).ToArray(), api.DataPath, api.Method,
            api.Format, api.Authentication, api.Username, api.Password, api.Token, api.ApiKeyName, api.ApiKey,
            api.OAuthTokenUrl, api.ClientId, api.ClientSecret, api.Scope, api.NextPath, api.UseLinkHeader, api.Body, api.Headers, api.UseLinkHeaderVariable),
        PostgresInputRecord postgres => new PostgresInputStep(postgres.Id, postgres.Host, postgres.Port, postgres.Database, postgres.Username,
            postgres.Password, postgres.SslMode, postgres.Query, postgres.Schema.Select(MapTo).ToArray()),
        _ => throw new ArgumentException("Unsupported step.")
    };
    public static IRowPredicate MapTo(this PredicateRecord predicate) => predicate switch
    {
        ComparisonRecord compare when compare.ColumnExpression is not null || compare.ValueExpression is not null => new DeferredComparisonPredicate(),
        ComparisonRecord compare => new ComparisonPredicate(compare.Column, Enum.Parse<ComparisonOperator>(compare.Operator, true), compare.Value),
        NullRecord check => new NullPredicate(check.Column, check.IsNull),
        LogicalRecord logical => new LogicalPredicate(Enum.Parse<LogicalOperator>(logical.Operator, true), logical.Children.Select(MapTo).ToArray()),
        _ => throw new ArgumentException("Unsupported expression.")
    };
    public static AnalysisRecord MapTo(this PipelineAnalysis analysis) => new(
        analysis.Issues.Select(i => new IssueRecord(i.BlockId, i.Message)).ToArray(),
        analysis.Schemas.Select(s => new SchemaRecord(s.BlockId, s.Input.Select(MapTo).ToArray(), s.Output.Select(MapTo).ToArray())).ToArray());
}

internal sealed class DeferredComparisonPredicate : IRowPredicate
{
    public bool Evaluate(IReadOnlyDictionary<string, object?> row, Column[] schema) =>
        throw new InvalidOperationException("Resolve comparison expressions before execution.");
    public void Validate(Column[] schema, List<PipelineIssue> issues, string blockId, int depth) { }
}
