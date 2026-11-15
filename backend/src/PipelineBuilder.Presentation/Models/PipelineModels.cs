using System.Text.Json;
using System.Text.Json.Serialization;
using PipelineBuilder.Application.Contracts;

namespace PipelineBuilder.Presentation.Models;

public sealed record ColumnModel(string Name, ColumnTypeRecord Type, bool Nullable = false, int Precision = 18, int Scale = 2)
{
    public ColumnRecord MapTo() => new(Name, Type, Nullable, Precision, Scale);
    public static ColumnModel MapFrom(ColumnRecord column) => new(column.Name, column.Type, column.Nullable, column.Precision, column.Scale);
}
public sealed record MappingModel(string Source, ColumnModel Target, string? Format = null);
public sealed record DefinitionModel([property: JsonRequired] int FormatVersion, StepModel[] Steps, VariableRecord[]? Variables = null)
{
    public DefinitionRecord MapTo() => new(FormatVersion, Steps?.Select(s => s?.MapTo()!).ToArray()!, Variables);
    public static DefinitionModel MapFrom(DefinitionRecord definition) => new(definition.FormatVersion, definition.Steps.Select(StepModel.MapFrom).ToArray(), definition.Variables);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CsvModel), "csv")]
[JsonDerivedType(typeof(MapModel), "map")]
[JsonDerivedType(typeof(FilterModel), "filter")]
[JsonDerivedType(typeof(ParquetModel), "parquet")]
[JsonDerivedType(typeof(CsvOutputModel), "csvOutput")]
[JsonDerivedType(typeof(ParquetInputModel), "parquetInput")]
[JsonDerivedType(typeof(ExcelInputModel), "excelInput")]
[JsonDerivedType(typeof(ExcelOutputModel), "excelOutput")]
public abstract record StepModel(string Id)
{
    public abstract StepRecord MapTo();
    public static StepModel MapFrom(StepRecord step) => step switch
    {
        CsvRecord csv => new CsvModel(csv.Id, csv.Path, csv.Schema.Select(ColumnModel.MapFrom).ToArray(), csv.Delimiter, csv.HasHeader, csv.PathExpression),
        MapRecord map => new MapModel(map.Id, map.Mappings.Select(m => new MappingModel(m.Source, ColumnModel.MapFrom(m.Target), m.Format)).ToArray()),
        FilterRecord filter => new FilterModel(filter.Id, PredicateModel.MapFrom(filter.Criteria)),
        ParquetRecord parquet => new ParquetModel(parquet.Id, parquet.Path, parquet.PathExpression),
        CsvOutputRecord csv => new CsvOutputModel(csv.Id, csv.Path, csv.Delimiter, csv.HasHeader, csv.PathExpression),
        ParquetInputRecord parquet => new ParquetInputModel(parquet.Id, parquet.Path, parquet.Schema.Select(ColumnModel.MapFrom).ToArray(), parquet.PathExpression),
        ExcelInputRecord excel => new ExcelInputModel(excel.Id, excel.Path, excel.Schema.Select(ColumnModel.MapFrom).ToArray(), excel.Sheet, excel.HasHeader, excel.PathExpression),
        ExcelOutputRecord excel => new ExcelOutputModel(excel.Id, excel.Path, excel.Sheet, excel.HasHeader, excel.PathExpression),
        _ => throw new ArgumentException("Unsupported step.")
    };
}
public sealed record CsvModel(string Id, string Path, ColumnModel[] Schema, string Delimiter = ",", bool HasHeader = true, TextExpressionRecord? PathExpression = null) : StepModel(Id)
{
    public override StepRecord MapTo() => new CsvRecord(Id, Path, Schema?.Select(c => c?.MapTo()!).ToArray()!, Delimiter, HasHeader, PathExpression);
}
public sealed record MapModel(string Id, MappingModel[] Mappings) : StepModel(Id)
{
    public override StepRecord MapTo() => new MapRecord(Id, Mappings?.Select(m => m is null ? null! : new MappingRecord(m.Source, m.Target?.MapTo()!, m.Format)).ToArray()!);
}
public sealed record FilterModel(string Id, PredicateModel Criteria) : StepModel(Id)
{
    public override StepRecord MapTo() => new FilterRecord(Id, Criteria?.MapTo()!);
}
public sealed record ParquetModel(string Id, string Path, TextExpressionRecord? PathExpression = null) : StepModel(Id)
{
    public override StepRecord MapTo() => new ParquetRecord(Id, Path, PathExpression);
}
public sealed record CsvOutputModel(string Id, string Path, string Delimiter = ",", bool HasHeader = true, TextExpressionRecord? PathExpression = null) : StepModel(Id)
{
    public override StepRecord MapTo() => new CsvOutputRecord(Id, Path, Delimiter, HasHeader, PathExpression);
}
public sealed record ParquetInputModel(string Id, string Path, ColumnModel[] Schema, TextExpressionRecord? PathExpression = null) : StepModel(Id)
{
    public override StepRecord MapTo() => new ParquetInputRecord(Id, Path, Schema?.Select(c => c?.MapTo()!).ToArray()!, PathExpression);
}
public sealed record ExcelInputModel(string Id, string Path, ColumnModel[] Schema, string Sheet, bool HasHeader = true, TextExpressionRecord? PathExpression = null) : StepModel(Id)
{
    public override StepRecord MapTo() => new ExcelInputRecord(Id, Path, Schema?.Select(c => c?.MapTo()!).ToArray()!, Sheet, HasHeader, PathExpression);
}
public sealed record ExcelOutputModel(string Id, string Path, string Sheet, bool HasHeader = true, TextExpressionRecord? PathExpression = null) : StepModel(Id)
{
    public override StepRecord MapTo() => new ExcelOutputRecord(Id, Path, Sheet, HasHeader, PathExpression);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ComparisonModel), "compare")]
[JsonDerivedType(typeof(NullModel), "null")]
[JsonDerivedType(typeof(LogicalModel), "logical")]
public abstract record PredicateModel
{
    public abstract PredicateRecord MapTo();
    public static PredicateModel MapFrom(PredicateRecord predicate) => predicate switch
    {
        ComparisonRecord compare => new ComparisonModel(compare.Column, compare.Operator, compare.Value, compare.ColumnExpression, compare.ValueExpression),
        NullRecord check => new NullModel(check.Column, check.IsNull),
        LogicalRecord logical => new LogicalModel(logical.Operator, logical.Children.Select(MapFrom).ToArray()),
        _ => throw new ArgumentException("Unsupported expression.")
    };
}
public sealed record ComparisonModel(string Column, string Operator, string Value, TextExpressionRecord? ColumnExpression = null, TextExpressionRecord? ValueExpression = null) : PredicateModel
{
    public override PredicateRecord MapTo() => new ComparisonRecord(Column, Operator, Value, ColumnExpression, ValueExpression);
}
public sealed record NullModel(string Column, [property: JsonRequired] bool IsNull) : PredicateModel
{
    public override PredicateRecord MapTo() => new NullRecord(Column, IsNull);
}
public sealed record LogicalModel(string Operator, PredicateModel[] Children) : PredicateModel
{
    public override PredicateRecord MapTo() => new LogicalRecord(Operator, Children?.Select(c => c?.MapTo()!).ToArray()!);
}

public sealed record DocumentModel(string Name, DefinitionModel Definition, [property: JsonRequired] JsonElement Workspace, Dictionary<string, Dictionary<string, string>> Environments)
{
    public DocumentRecord MapTo() => new(Name, Definition?.MapTo()!, Workspace, Environments);
    public static DocumentModel MapFrom(DocumentRecord document) => new(document.Name, DefinitionModel.MapFrom(document.Definition), document.Workspace, document.Environments);
}
public sealed record SavePipelineRequest([property: JsonRequired] int ExpectedRevision, DocumentModel Document);
public sealed record PipelineResponse(Guid Id, int Revision, DocumentModel Document)
{
    public static PipelineResponse MapFrom(SavedPipelineRecord pipeline) => new(pipeline.Id, pipeline.Revision, DocumentModel.MapFrom(pipeline.Document));
}
public sealed record PipelineSummaryResponse(Guid Id, string Name, int Revision, string[] Environments)
{
    public static PipelineSummaryResponse MapFrom(PipelineSummaryRecord pipeline) => new(pipeline.Id, pipeline.Name, pipeline.Revision, pipeline.Environments);
}
public sealed record PipelineListResponse(PipelineSummaryResponse[] Pipelines, int Page, bool HasMore);
public sealed record DeletePipelineResponse(bool Deleted);
public sealed record IssueModel(string BlockId, string Message);
public sealed record SchemaModel(string BlockId, ColumnModel[] Input, ColumnModel[] Output);
public sealed record AnalysisResponse(IssueModel[] Issues, SchemaModel[] Schemas)
{
    public static AnalysisResponse MapFrom(AnalysisRecord analysis) => new(analysis.Issues.Select(i => new IssueModel(i.BlockId, i.Message)).ToArray(),
        analysis.Schemas.Select(s => new SchemaModel(s.BlockId, s.Input.Select(ColumnModel.MapFrom).ToArray(), s.Output.Select(ColumnModel.MapFrom).ToArray())).ToArray());
}
public sealed record DiscoverSchemaRequest(string Path, string Delimiter = ",", bool HasHeader = true);
public sealed record DiscoverSchemaResponse(ColumnModel[] Schema, int SampleLimit);
public sealed record StartRunRequest([property: JsonRequired] Guid PipelineId, [property: JsonRequired] int Revision, string Environment);
public sealed record RunHistoryResponse(RunResponse[] Runs, int Page, bool HasMore);
public sealed record RunResponse(Guid Id, Guid PipelineId, int Revision, string Environment, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, long RowsRead, long RowsWritten, string? Error, string? BlockId, string Trigger)
{
    public static RunResponse MapFrom(RunRecord run) => new(run.Id, run.PipelineId, run.Revision, run.Environment, run.Status,
        run.CreatedAt, run.StartedAt, run.FinishedAt, run.RowsRead, run.RowsWritten, run.Error, run.BlockId, run.Trigger);
}
