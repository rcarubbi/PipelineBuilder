namespace PipelineBuilder.Domain.Pipelines;

public enum ColumnType { String, Boolean, Int64, Decimal, Double, Date, Timestamp }
public sealed record Column(string Name, ColumnType Type, bool Nullable = false, int Precision = 18, int Scale = 2);
public sealed record ColumnMapping(string Source, Column Target, string? Format = null);
public abstract record PipelineStep(string Id);
public sealed record CsvStep(string Id, string Path, Column[] Schema, string Delimiter = ",", bool HasHeader = true, string? HasHeaderVariable = null) : PipelineStep(Id);
public sealed record ParquetInputStep(string Id, string Path, Column[] Schema) : PipelineStep(Id);
public sealed record ExcelInputStep(string Id, string Path, Column[] Schema, string Sheet, bool HasHeader = true, string? HasHeaderVariable = null) : PipelineStep(Id);
public sealed record ApiInputStep(string Id, string Url, Column[] Schema, string DataPath = "", string Method = "GET",
    string Format = "auto", string Authentication = "none", string? Username = null, string? Password = null,
    string? Token = null, string? ApiKeyName = null, string? ApiKey = null, string? OAuthTokenUrl = null,
    string? ClientId = null, string? ClientSecret = null, string? Scope = null,
    string? NextPath = null, bool UseLinkHeader = false, string? Body = null, Dictionary<string, string>? Headers = null,
    string? UseLinkHeaderVariable = null) : PipelineStep(Id);
public sealed record MapStep(string Id, ColumnMapping[] Mappings) : PipelineStep(Id);
public sealed record FilterStep(string Id, IRowPredicate Criteria) : PipelineStep(Id);
public sealed record ParquetStep(string Id, string Path) : PipelineStep(Id);
public sealed record CsvOutputStep(string Id, string Path, string Delimiter = ",", bool HasHeader = true, string? HasHeaderVariable = null) : PipelineStep(Id);
public sealed record ExcelOutputStep(string Id, string Path, string Sheet, bool HasHeader = true, string? HasHeaderVariable = null) : PipelineStep(Id);
public sealed record PipelineDefinition(int FormatVersion, PipelineStep[] Steps);
public sealed record PipelineIssue(string BlockId, string Message);
public sealed record StepSchema(string BlockId, Column[] Input, Column[] Output);
public sealed record PipelineAnalysis(PipelineIssue[] Issues, StepSchema[] Schemas);

public static class PipelineValidation
{
    public static PipelineAnalysis Analyze(PipelineDefinition definition)
    {
        List<PipelineIssue> issues = [];
        List<StepSchema> schemas = [];
        if (definition.FormatVersion != 1) issues.Add(new("", "Unsupported definition format version."));
        if (definition.Steps.Length is < 2 or > 100) issues.Add(new("", "A pipeline needs 2 to 100 steps."));
        if (definition.Steps.FirstOrDefault() is not (CsvStep or ParquetInputStep or ExcelInputStep or ApiInputStep) || definition.Steps.LastOrDefault() is not (ParquetStep or CsvOutputStep or ExcelOutputStep))
            issues.Add(new("", "Connect one input to one final output."));
        HashSet<string> ids = [];
        Column[] current = [];
        for (var index = 0; index < definition.Steps.Length; index++)
        {
            var step = definition.Steps[index];
            var input = current;
            if (string.IsNullOrWhiteSpace(step.Id) || !ids.Add(step.Id)) issues.Add(new(step.Id, "Block IDs must be present and unique."));
            switch (step)
            {
                case CsvStep csv:
                    if (index != 0) issues.Add(new(step.Id, "CSV input must be first and unique."));
                    if (string.IsNullOrWhiteSpace(csv.Path)) issues.Add(new(step.Id, "Input path is required."));
                    if (!HasVariable(csv.Delimiter) && (csv.Delimiter.Length != 1 || csv.Delimiter is "\r" or "\n" or "\"")) issues.Add(new(step.Id, "Choose one valid delimiter character."));
                    current = csv.Schema;
                    break;
                case MapStep map:
                    foreach (var mapping in map.Mappings.Where(mapping => !current.Any(c => c.Name == mapping.Source)))
                        issues.Add(new(step.Id, $"Unknown source column '{mapping.Source}'."));
                    current = map.Mappings.Select(m => m.Target).ToArray();
                    break;
                case FilterStep filter:
                    filter.Criteria.Validate(current, issues, step.Id, 0);
                    break;
                case ParquetStep parquet:
                    if (index != definition.Steps.Length - 1) issues.Add(new(step.Id, "Parquet output must be last and unique."));
                    if (string.IsNullOrWhiteSpace(parquet.Path)) issues.Add(new(step.Id, "Output path is required."));
                    break;
                case ParquetInputStep parquetInput:
                    if (index != 0 || string.IsNullOrWhiteSpace(parquetInput.Path)) issues.Add(new(step.Id, "Parquet input must be first and have a path."));
                    current = parquetInput.Schema;
                    break;
                case ExcelInputStep excelInput:
                    if (index != 0 || string.IsNullOrWhiteSpace(excelInput.Path) || string.IsNullOrWhiteSpace(excelInput.Sheet)) issues.Add(new(step.Id, "Excel input must be first and have a path and sheet name."));
                    current = excelInput.Schema;
                    break;
                case ApiInputStep api:
                    if (index != 0 || !HasVariable(api.Url) && (!Uri.TryCreate(api.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
                        issues.Add(new(step.Id, "API input needs an absolute HTTP or HTTPS URL."));
                    if (api.Url.Length > 4096 || api.DataPath.Length > 512 || (api.NextPath?.Length ?? 0) > 512)
                        issues.Add(new(step.Id, "API URL and response paths exceed their length limits."));
                    if ((api.Body?.Length ?? 0) > 1_048_576 || api.Headers?.Count > 50)
                        issues.Add(new(step.Id, "API request body is limited to 1 MiB and custom headers to 50."));
                    if (!HasVariable(api.Method) && api.Method is not ("GET" or "POST")) issues.Add(new(step.Id, "API method must be GET or POST."));
                    if (!HasVariable(api.Format) && api.Format is not ("auto" or "json" or "xml")) issues.Add(new(step.Id, "API format must be auto, json or xml."));
                    if (!HasVariable(api.Authentication) && api.Authentication is not ("none" or "basic" or "bearer" or "apiKey" or "oauth2"))
                        issues.Add(new(step.Id, "Unsupported API authentication mode."));
                    if (api.Authentication == "oauth2" && (string.IsNullOrWhiteSpace(api.OAuthTokenUrl) || string.IsNullOrWhiteSpace(api.ClientId) || string.IsNullOrWhiteSpace(api.ClientSecret)))
                        issues.Add(new(step.Id, "OAuth 2.0 Client Credentials requires token URL, client ID and client secret."));
                    if (api.Authentication == "basic" && !IsSecretReference(api.Password) ||
                        api.Authentication == "bearer" && !IsSecretReference(api.Token) ||
                        api.Authentication == "apiKey" && !IsSecretReference(api.ApiKey) ||
                        api.Authentication == "oauth2" && !IsSecretReference(api.ClientSecret))
                        issues.Add(new(step.Id, "Credentials must use a server-side ${secret.NAME} reference."));
                    if (api.Headers?.Any(header => header.Key.Length > 128 || header.Value.Length > 4096 ||
                        (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || header.Key.Contains("api-key", StringComparison.OrdinalIgnoreCase))
                        && !IsSecretReference(header.Value)) == true)
                        issues.Add(new(step.Id, "Sensitive custom headers must use ${secret.NAME}; header values are limited to 4096 characters."));
                    current = api.Schema;
                    break;
                case CsvOutputStep csvOutput:
                    if (index != definition.Steps.Length - 1) issues.Add(new(step.Id, "CSV output must be last and unique."));
                    if (string.IsNullOrWhiteSpace(csvOutput.Path)) issues.Add(new(step.Id, "Output path is required."));
                    if (!HasVariable(csvOutput.Delimiter) && (csvOutput.Delimiter.Length != 1 || csvOutput.Delimiter is "\r" or "\n" or "\"")) issues.Add(new(step.Id, "Choose one valid delimiter character."));
                    break;
                case ExcelOutputStep excelOutput:
                    if (index != definition.Steps.Length - 1 || string.IsNullOrWhiteSpace(excelOutput.Path) || string.IsNullOrWhiteSpace(excelOutput.Sheet)) issues.Add(new(step.Id, "Excel output must be last and have a path and sheet name."));
                    break;
                default:
                    issues.Add(new(step.Id, "Unsupported block."));
                    break;
            }
            if ((current.Length is < 1 or > 200) && step is not (ParquetStep or CsvOutputStep or ExcelOutputStep)) issues.Add(new(step.Id, "Schemas need 1 to 200 columns."));
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (var column in current)
            {
                if (string.IsNullOrWhiteSpace(column.Name) || !names.Add(column.Name)) issues.Add(new(step.Id, "Column names must be present and unique."));
                if (!Enum.IsDefined(column.Type)) issues.Add(new(step.Id, "Unknown column type."));
                if (column.Type == ColumnType.Decimal && (column.Precision is < 1 or > 28 || column.Scale < 0 || column.Scale > column.Precision))
                    issues.Add(new(step.Id, "Decimal precision must be 1–28; scale must be between zero and precision."));
            }
            schemas.Add(new(step.Id, input, step is ParquetStep or CsvOutputStep or ExcelOutputStep ? [] : current));
        }
        return new(issues.ToArray(), schemas.ToArray());
    }

    private static bool IsSecretReference(string? value) =>
        value is not null && value.StartsWith("${secret.", StringComparison.Ordinal) && value.EndsWith('}') &&
        value.Length > 10 && value[9..^1].All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    private static bool HasVariable(string? value) => value?.Contains("${", StringComparison.Ordinal) == true;
}
