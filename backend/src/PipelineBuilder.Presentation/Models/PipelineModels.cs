using System.Text.Json;
using System.Text.Json.Serialization;
using PipelineBuilder.Application.Contracts;

namespace PipelineBuilder.Presentation.Models;

public sealed record ColumnModel(string Name, ColumnTypeRecord Type, bool Nullable = false, int Precision = 18, int Scale = 2,
    string? TypeVariable = null, string? NullableVariable = null, string? PrecisionVariable = null, string? ScaleVariable = null)
{
    public ColumnRecord MapTo() => new(Name, Type, Nullable, Precision, Scale, TypeVariable, NullableVariable, PrecisionVariable, ScaleVariable);
    public static ColumnModel MapFrom(ColumnRecord column) => new(column.Name, column.Type, column.Nullable, column.Precision, column.Scale,
        column.TypeVariable, column.NullableVariable, column.PrecisionVariable, column.ScaleVariable);
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
[JsonDerivedType(typeof(ApiInputModel), "apiInput")]
public abstract record StepModel(string Id)
{
    public abstract StepRecord MapTo();
    public static StepModel MapFrom(StepRecord step) => step switch
    {
        CsvRecord csv => new CsvModel(csv.Id, csv.Path, csv.Schema.Select(ColumnModel.MapFrom).ToArray(), csv.Delimiter, csv.HasHeader, csv.PathExpression, csv.HasHeaderVariable),
        MapRecord map => new MapModel(map.Id, map.Mappings.Select(m => new MappingModel(m.Source, ColumnModel.MapFrom(m.Target), m.Format)).ToArray()),
        FilterRecord filter => new FilterModel(filter.Id, PredicateModel.MapFrom(filter.Criteria)),
        ParquetRecord parquet => new ParquetModel(parquet.Id, parquet.Path, parquet.PathExpression),
        CsvOutputRecord csv => new CsvOutputModel(csv.Id, csv.Path, csv.Delimiter, csv.HasHeader, csv.PathExpression, csv.HasHeaderVariable),
        ParquetInputRecord parquet => new ParquetInputModel(parquet.Id, parquet.Path, parquet.Schema.Select(ColumnModel.MapFrom).ToArray(), parquet.PathExpression),
        ExcelInputRecord excel => new ExcelInputModel(excel.Id, excel.Path, excel.Schema.Select(ColumnModel.MapFrom).ToArray(), excel.Sheet, excel.HasHeader, excel.PathExpression, excel.HasHeaderVariable),
        ExcelOutputRecord excel => new ExcelOutputModel(excel.Id, excel.Path, excel.Sheet, excel.HasHeader, excel.PathExpression, excel.HasHeaderVariable),
        ApiInputRecord api => new ApiInputModel(api.Id, api.Url, api.Schema.Select(ColumnModel.MapFrom).ToArray(), api.DataPath,
            api.Method, api.Format, api.Authentication, api.Username, api.Password, api.Token, api.ApiKeyName, api.ApiKey,
            api.OAuthTokenUrl, api.ClientId, api.ClientSecret, api.Scope, api.NextPath, api.UseLinkHeader, api.Body, api.Headers, api.UseLinkHeaderVariable),
        _ => throw new ArgumentException("Unsupported step.")
    };
}
public sealed record CsvModel(string Id, string Path, ColumnModel[] Schema, string Delimiter = ",", bool HasHeader = true, TextExpressionRecord? PathExpression = null, string? HasHeaderVariable = null) : StepModel(Id)
{
    public override StepRecord MapTo() => new CsvRecord(Id, Path, Schema?.Select(c => c?.MapTo()!).ToArray()!, Delimiter, HasHeader, PathExpression, HasHeaderVariable);
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
public sealed record CsvOutputModel(string Id, string Path, string Delimiter = ",", bool HasHeader = true, TextExpressionRecord? PathExpression = null, string? HasHeaderVariable = null) : StepModel(Id)
{
    public override StepRecord MapTo() => new CsvOutputRecord(Id, Path, Delimiter, HasHeader, PathExpression, HasHeaderVariable);
}
public sealed record ParquetInputModel(string Id, string Path, ColumnModel[] Schema, TextExpressionRecord? PathExpression = null) : StepModel(Id)
{
    public override StepRecord MapTo() => new ParquetInputRecord(Id, Path, Schema?.Select(c => c?.MapTo()!).ToArray()!, PathExpression);
}
public sealed record ExcelInputModel(string Id, string Path, ColumnModel[] Schema, string Sheet, bool HasHeader = true, TextExpressionRecord? PathExpression = null, string? HasHeaderVariable = null) : StepModel(Id)
{
    public override StepRecord MapTo() => new ExcelInputRecord(Id, Path, Schema?.Select(c => c?.MapTo()!).ToArray()!, Sheet, HasHeader, PathExpression, HasHeaderVariable);
}
public sealed record ExcelOutputModel(string Id, string Path, string Sheet, bool HasHeader = true, TextExpressionRecord? PathExpression = null, string? HasHeaderVariable = null) : StepModel(Id)
{
    public override StepRecord MapTo() => new ExcelOutputRecord(Id, Path, Sheet, HasHeader, PathExpression, HasHeaderVariable);
}
public sealed record ApiInputModel(string Id, string Url, ColumnModel[] Schema, string DataPath = "", string Method = "GET",
    string Format = "auto", string Authentication = "none", string? Username = null, string? Password = null,
    string? Token = null, string? ApiKeyName = null, string? ApiKey = null, string? OAuthTokenUrl = null,
    string? ClientId = null, string? ClientSecret = null, string? Scope = null,
    string? NextPath = null, bool UseLinkHeader = false, string? Body = null, Dictionary<string, string>? Headers = null,
    string? UseLinkHeaderVariable = null) : StepModel(Id)
{
    public override StepRecord MapTo() => new ApiInputRecord(Id, Url, Schema?.Select(c => c.MapTo()).ToArray()!, DataPath,
        Method, Format, Authentication, Username, Password, Token, ApiKeyName, ApiKey, OAuthTokenUrl, ClientId, ClientSecret, Scope, NextPath, UseLinkHeader, Body, Headers, UseLinkHeaderVariable);
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
        ComparisonRecord compare => new ComparisonModel(compare.Column, compare.Operator, compare.Value, compare.ColumnExpression, compare.ValueExpression, compare.OperatorVariable),
        NullRecord check => new NullModel(check.Column, check.IsNull, check.IsNullVariable),
        LogicalRecord logical => new LogicalModel(logical.Operator, logical.Children.Select(MapFrom).ToArray()),
        _ => throw new ArgumentException("Unsupported expression.")
    };
}
public sealed record ComparisonModel(string Column, string Operator, string Value, TextExpressionRecord? ColumnExpression = null,
    TextExpressionRecord? ValueExpression = null, string? OperatorVariable = null) : PredicateModel
{
    public override PredicateRecord MapTo() => new ComparisonRecord(Column, Operator, Value, ColumnExpression, ValueExpression, OperatorVariable);
}
public sealed record NullModel(string Column, [property: JsonRequired] bool IsNull, string? IsNullVariable = null) : PredicateModel
{
    public override PredicateRecord MapTo() => new NullRecord(Column, IsNull, IsNullVariable);
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
public sealed record DiscoverSchemaRequest(string Path = "", string Delimiter = ",", bool HasHeader = true, string Format = "csv", string? Sheet = null, ApiInputRecord? Api = null);
public sealed record DiscoverSchemaResponse(ColumnModel[] Schema, int SampleLimit, string[]? DataPaths = null);
public sealed record StartRunRequest([property: JsonRequired] Guid PipelineId, [property: JsonRequired] int Revision, string Environment);
public sealed record RunHistoryResponse(RunResponse[] Runs, int Page, bool HasMore);
public sealed record RunResponse(Guid Id, Guid PipelineId, int Revision, string Environment, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, long RowsRead, long RowsWritten, string? Error, string? BlockId, string Trigger)
{
    public static RunResponse MapFrom(RunRecord run) => new(run.Id, run.PipelineId, run.Revision, run.Environment, run.Status,
        run.CreatedAt, run.StartedAt, run.FinishedAt, run.RowsRead, run.RowsWritten, run.Error, run.BlockId, run.Trigger);
}
