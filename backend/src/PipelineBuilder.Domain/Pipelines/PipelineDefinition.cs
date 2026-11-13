namespace PipelineBuilder.Domain.Pipelines;

public enum ColumnType { String, Boolean, Int64, Decimal, Double, Date, Timestamp }
public sealed record Column(string Name, ColumnType Type, bool Nullable = false, int Precision = 18, int Scale = 2);
public sealed record ColumnMapping(string Source, Column Target, string? Format = null);
public abstract record PipelineStep(string Id);
public sealed record CsvStep(string Id, string Path, Column[] Schema, string Delimiter = ",", bool HasHeader = true) : PipelineStep(Id);
public sealed record MapStep(string Id, ColumnMapping[] Mappings) : PipelineStep(Id);
public sealed record FilterStep(string Id, IRowPredicate Criteria) : PipelineStep(Id);
public sealed record ParquetStep(string Id, string Path) : PipelineStep(Id);
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
        if (definition.Steps.FirstOrDefault() is not CsvStep || definition.Steps.LastOrDefault() is not ParquetStep)
            issues.Add(new("", "Connect one CSV input to one final Parquet output."));
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
                    if (csv.Delimiter.Length != 1 || csv.Delimiter is "\r" or "\n" or "\"") issues.Add(new(step.Id, "Choose one valid delimiter character."));
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
                default:
                    issues.Add(new(step.Id, "Unsupported block."));
                    break;
            }
            if (current.Length is < 1 or > 200) issues.Add(new(step.Id, "Schemas need 1 to 200 columns."));
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (var column in current)
            {
                if (string.IsNullOrWhiteSpace(column.Name) || !names.Add(column.Name)) issues.Add(new(step.Id, "Column names must be present and unique."));
                if (!Enum.IsDefined(column.Type)) issues.Add(new(step.Id, "Unknown column type."));
                if (column.Type == ColumnType.Decimal && (column.Precision is < 1 or > 28 || column.Scale < 0 || column.Scale > column.Precision))
                    issues.Add(new(step.Id, "Decimal precision must be 1–28; scale must be between zero and precision."));
            }
            schemas.Add(new(step.Id, input, step is ParquetStep ? [] : current));
        }
        return new(issues.ToArray(), schemas.ToArray());
    }
}
