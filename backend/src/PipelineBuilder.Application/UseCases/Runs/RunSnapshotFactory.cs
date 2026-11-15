using System.Text.RegularExpressions;
using System.Globalization;
using ErrorOr;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Application.UseCases.Runs;

internal static partial class RunSnapshotFactory
{
    public const string EngineVersion = "1.0.0";

    public static ErrorOr<RunSnapshotRecord> Create(SavedPipelineRecord pipeline, string environment, string trigger)
    {
        if (!pipeline.Document.Environments.TryGetValue(environment, out var variables)) return Error.Validation("Environment", "Select an existing environment.");
        try
        {
            var now = DateTimeOffset.UtcNow;
            var declarations = new Dictionary<string, TextExpressionRecord>(StringComparer.Ordinal);
            foreach (var variable in pipeline.Document.Definition.Variables ?? [])
            {
                if (variable is null || string.IsNullOrWhiteSpace(variable.Name) || variable.Value is null ||
                    !declarations.TryAdd(variable.Name, variable.Value))
                    throw new PipelineDataException("Variable names must be nonempty and unique.");
            }
            var active = new HashSet<string>(StringComparer.Ordinal);
            string Evaluate(TextExpressionRecord? expression, int depth)
            {
                if (expression is null || depth >= 32) throw new PipelineDataException("Invalid or excessively nested text expression.", expression?.Id);
                switch (expression)
                {
                    case TextLiteralRecord literal when literal.Value is not null: return literal.Value;
                    case TextConcatRecord concat:
                        var left = Evaluate(concat.Left, depth + 1);
                        var right = Evaluate(concat.Right, depth + 1);
                        if (left.Length + (long)right.Length > 4096) throw new PipelineDataException("Text expression exceeds 4096 characters.", concat.Id);
                        return left + right;
                    case TextEnvironmentRecord { Name: "CURRENT_ENVIRONMENT" }: return environment;
                    case TextEnvironmentRecord env when env.Name is not null && variables.TryGetValue(env.Name, out var value): return value;
                    case TextVariableRecord variable when variable.Name is not null && declarations.TryGetValue(variable.Name, out var definition):
                        if (!active.Add(variable.Name)) throw new PipelineDataException($"Circular variable reference '{variable.Name}'.", variable.Id);
                        try { return Evaluate(definition, depth + 1); }
                        finally { active.Remove(variable.Name); }
                    case TextUtcRecord utc:
                        var format = utc.FormatExpression is null ? utc.Format : Evaluate(utc.FormatExpression, depth + 1);
                        if (string.IsNullOrEmpty(format)) throw new PipelineDataException("UTC date format is required.", utc.Id);
                        try { return now.ToString(format, CultureInfo.InvariantCulture); }
                        catch (FormatException ex) { throw new PipelineDataException("Invalid UTC date format.", utc.Id, ex); }
                    default: throw new PipelineDataException("Missing variable or invalid text expression.", expression.Id);
                }
            }
            string Resolve(string path) => VariablePattern().Replace(path, match =>
            {
                var name = match.Groups[1].Value;
                if (name == "CURRENT_ENVIRONMENT") return environment;
                return variables.TryGetValue(name, out var value) ? value
                    : throw new PipelineDataException($"Missing environment variable '{name}'.");
            });
            PredicateRecord ResolvePredicate(PredicateRecord predicate) => predicate switch
            {
                ComparisonRecord comparison => comparison with
                {
                    Column = comparison.ColumnExpression is null ? comparison.Column : Evaluate(comparison.ColumnExpression, 0),
                    Value = comparison.ValueExpression is null ? comparison.Value : Evaluate(comparison.ValueExpression, 0),
                    ColumnExpression = null,
                    ValueExpression = null
                },
                LogicalRecord logical => logical with { Children = logical.Children.Select(ResolvePredicate).ToArray() },
                _ => predicate
            };
            var definition = pipeline.Document.Definition with
            {
                Steps = pipeline.Document.Definition.Steps.Select(step => step switch
                {
                    CsvRecord csv => (StepRecord)(csv with { Path = csv.PathExpression is null ? Resolve(csv.Path) : Evaluate(csv.PathExpression, 0), PathExpression = null }),
                    ParquetRecord output => output with { Path = output.PathExpression is null ? Resolve(output.Path) : Evaluate(output.PathExpression, 0), PathExpression = null },
                    CsvOutputRecord output => output with { Path = output.PathExpression is null ? Resolve(output.Path) : Evaluate(output.PathExpression, 0), PathExpression = null },
                    ParquetInputRecord input => input with { Path = input.PathExpression is null ? Resolve(input.Path) : Evaluate(input.PathExpression, 0), PathExpression = null },
                    ExcelInputRecord input => input with { Path = input.PathExpression is null ? Resolve(input.Path) : Evaluate(input.PathExpression, 0), PathExpression = null },
                    ExcelOutputRecord output => output with { Path = output.PathExpression is null ? Resolve(output.Path) : Evaluate(output.PathExpression, 0), PathExpression = null },
                    FilterRecord filter => filter with { Criteria = ResolvePredicate(filter.Criteria) },
                    _ => step
                }).ToArray()
            };
            var analysis = PipelineValidation.Analyze(definition.MapTo());
            if (analysis.Issues.Length > 0) return analysis.Issues.Select(i => Error.Validation(i.BlockId, i.Message)).ToList();
            return new RunSnapshotRecord(definition, environment, new(variables), EngineVersion, trigger);
        }
        catch (PipelineDataException ex) { return Error.Validation(ex.BlockId ?? "Run.Configuration", ex.Message); }
    }

    [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();
}
