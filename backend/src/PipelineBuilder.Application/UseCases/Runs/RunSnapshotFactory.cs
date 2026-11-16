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
            var configuredVariables = pipeline.Document.Definition.Variables ?? [];
            var validVariables = configuredVariables.OfType<VariableRecord>()
                .Where(variable => !string.IsNullOrWhiteSpace(variable.Name) && variable.Value is not null)
                .ToArray();
            if (validVariables.Length != configuredVariables.Length ||
                validVariables.Select(variable => variable.Name).Distinct(StringComparer.Ordinal).Count() != validVariables.Length)
                throw new PipelineDataException("Variable names must be nonempty and unique.");
            var declarations = validVariables.ToDictionary(variable => variable.Name, variable => variable.Value, StringComparer.Ordinal);
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
                var scope = match.Groups[1].Value;
                var name = match.Groups[2].Value;
                if (scope == "secret") return match.Value;
                if (scope == "var")
                {
                    if (!declarations.TryGetValue(name, out var declaration)) throw new PipelineDataException($"Missing pipeline variable '{name}'.");
                    if (!active.Add(name)) throw new PipelineDataException($"Circular variable reference '{name}'.");
                    try { return Evaluate(declaration, 0); }
                    finally { active.Remove(name); }
                }
                if (scope == "env") return variables.TryGetValue(name, out var scopedValue)
                    ? scopedValue : throw new PipelineDataException($"Missing environment variable '{name}'.");
                if (name == "CURRENT_ENVIRONMENT") return environment;
                return variables.TryGetValue(name, out var value) ? value
                    : throw new PipelineDataException($"Missing environment variable '{name}'.");
            });
            bool ResolveBoolean(bool value, string? expression)
            {
                if (expression is null) return value;
                if (bool.TryParse(Resolve(expression), out var resolved)) return resolved;
                throw new PipelineDataException("Boolean property variable must resolve to 'true' or 'false'.");
            }
            PredicateRecord ResolvePredicate(PredicateRecord predicate)
            {
                switch (predicate)
                {
                    case ComparisonRecord comparison:
                        var operatorName = comparison.OperatorVariable is null
                            ? comparison.Operator
                            : Resolve(comparison.OperatorVariable);
                        if (!Enum.TryParse<ComparisonOperator>(operatorName, true, out var comparisonOperator) ||
                            !Enum.IsDefined(comparisonOperator))
                            throw new PipelineDataException("Comparison operator variable did not resolve to a supported operator.");
                        return comparison with
                        {
                            Column = comparison.ColumnExpression is null ? Resolve(comparison.Column) : Evaluate(comparison.ColumnExpression, 0),
                            Value = comparison.ValueExpression is null ? Resolve(comparison.Value) : Evaluate(comparison.ValueExpression, 0),
                            Operator = operatorName,
                            ColumnExpression = null,
                            ValueExpression = null,
                            OperatorVariable = null
                        };
                    case NullRecord check:
                        return check with
                        {
                            Column = Resolve(check.Column),
                            IsNull = ResolveBoolean(check.IsNull, check.IsNullVariable),
                            IsNullVariable = null
                        };
                    case LogicalRecord logical:
                        return logical with { Children = logical.Children.Select(ResolvePredicate).ToArray() };
                    default:
                        return predicate;
                }
            }
            ColumnRecord ResolveColumn(ColumnRecord column)
            {
                var type = column.Type;
                if (column.TypeVariable is not null &&
                    (!Enum.TryParse(Resolve(column.TypeVariable), true, out type) || !Enum.IsDefined(type)))
                    throw new PipelineDataException("Column type variable did not resolve to a supported type.");
                var nullable = ResolveBoolean(column.Nullable, column.NullableVariable);
                var precision = ResolveInteger(column.Precision, column.PrecisionVariable);
                var scale = ResolveInteger(column.Scale, column.ScaleVariable);
                return column with
                {
                    Name = Resolve(column.Name), Type = type, Nullable = nullable, Precision = precision, Scale = scale,
                    TypeVariable = null, NullableVariable = null, PrecisionVariable = null, ScaleVariable = null
                };
            }
            int ResolveInteger(int value, string? expression)
            {
                if (expression is null) return value;
                if (int.TryParse(Resolve(expression), NumberStyles.Integer, CultureInfo.InvariantCulture, out var resolved)) return resolved;
                throw new PipelineDataException("Integer property variable did not resolve to a whole number.");
            }
            var definition = pipeline.Document.Definition with
            {
                Steps = pipeline.Document.Definition.Steps.Select(step => step switch
                {
                    CsvRecord csv => (StepRecord)(csv with { Path = csv.PathExpression is null ? Resolve(csv.Path) : Evaluate(csv.PathExpression, 0), Delimiter = Resolve(csv.Delimiter), HasHeader = ResolveBoolean(csv.HasHeader, csv.HasHeaderVariable), HasHeaderVariable = null, Schema = csv.Schema.Select(ResolveColumn).ToArray(), PathExpression = null }),
                    ParquetRecord output => output with { Path = output.PathExpression is null ? Resolve(output.Path) : Evaluate(output.PathExpression, 0), PathExpression = null },
                    CsvOutputRecord output => output with { Path = output.PathExpression is null ? Resolve(output.Path) : Evaluate(output.PathExpression, 0), Delimiter = Resolve(output.Delimiter), HasHeader = ResolveBoolean(output.HasHeader, output.HasHeaderVariable), HasHeaderVariable = null, PathExpression = null },
                    ParquetInputRecord input => input with { Path = input.PathExpression is null ? Resolve(input.Path) : Evaluate(input.PathExpression, 0), Schema = input.Schema.Select(ResolveColumn).ToArray(), PathExpression = null },
                    ExcelInputRecord input => input with { Path = input.PathExpression is null ? Resolve(input.Path) : Evaluate(input.PathExpression, 0), Sheet = Resolve(input.Sheet), HasHeader = ResolveBoolean(input.HasHeader, input.HasHeaderVariable), HasHeaderVariable = null, Schema = input.Schema.Select(ResolveColumn).ToArray(), PathExpression = null },
                    ExcelOutputRecord output => output with { Path = output.PathExpression is null ? Resolve(output.Path) : Evaluate(output.PathExpression, 0), Sheet = Resolve(output.Sheet), HasHeader = ResolveBoolean(output.HasHeader, output.HasHeaderVariable), HasHeaderVariable = null, PathExpression = null },
                    MapRecord map => map with { Mappings = map.Mappings.Select(m => m with { Source = Resolve(m.Source), Target = ResolveColumn(m.Target), Format = m.Format is null ? null : Resolve(m.Format) }).ToArray() },
                    ApiInputRecord api => api with
                    {
                        Url = Resolve(api.Url), Method = Resolve(api.Method), Format = Resolve(api.Format),
                        Authentication = Resolve(api.Authentication), DataPath = Resolve(api.DataPath), Username = Resolve(api.Username ?? ""),
                        Password = Resolve(api.Password ?? ""), Token = Resolve(api.Token ?? ""),
                        ApiKeyName = Resolve(api.ApiKeyName ?? ""), ApiKey = Resolve(api.ApiKey ?? ""),
                        OAuthTokenUrl = Resolve(api.OAuthTokenUrl ?? ""), ClientId = Resolve(api.ClientId ?? ""),
                        ClientSecret = Resolve(api.ClientSecret ?? ""), Scope = Resolve(api.Scope ?? ""),
                        NextPath = Resolve(api.NextPath ?? ""), Body = Resolve(api.Body ?? ""),
                        UseLinkHeader = ResolveBoolean(api.UseLinkHeader, api.UseLinkHeaderVariable), UseLinkHeaderVariable = null,
                        Headers = api.Headers?.ToDictionary(p => Resolve(p.Key), p => Resolve(p.Value), StringComparer.OrdinalIgnoreCase),
                        Schema = api.Schema.Select(ResolveColumn).ToArray()
                    },
                    PostgresInputRecord postgres => postgres with
                    {
                        Host = Resolve(postgres.Host), Port = Resolve(postgres.Port), Database = Resolve(postgres.Database),
                        Username = Resolve(postgres.Username), Password = Resolve(postgres.Password), SslMode = Resolve(postgres.SslMode),
                        Query = Resolve(postgres.Query), Schema = postgres.Schema.Select(ResolveColumn).ToArray()
                    },
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

    [GeneratedRegex(@"\$\{(?:(var|env|secret)\.)?([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();
}
