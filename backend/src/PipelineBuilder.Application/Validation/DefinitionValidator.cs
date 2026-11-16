using FluentValidation;
using PipelineBuilder.Application.Contracts;

namespace PipelineBuilder.Application.Validation;

public sealed class DefinitionValidator : AbstractValidator<DefinitionRecord>
{
    public DefinitionValidator()
    {
        RuleFor(x => x.FormatVersion).Equal(1);
        RuleFor(x => x.Steps).NotNull().Must(s => s is { Length: >= 2 and <= 100 }).WithMessage("Use 2 to 100 steps.");
        RuleForEach(x => x.Steps).Custom((step, context) =>
        {
            if (step is null) { context.AddFailure("Steps cannot be null."); return; }
            if (string.IsNullOrWhiteSpace(step.Id)) context.AddFailure("Block ID is required.");
            switch (step)
            {
                case CsvRecord csv:
                    if (csv.Path is null || csv.Delimiter is null || !ValidColumns(csv.Schema)) context.AddFailure("CSV path, delimiter and a valid schema are required.");
                    break;
                case MapRecord map:
                    if (map.Mappings is null || map.Mappings.Any(m => m is null || m.Source is null || m.Target is null) ||
                        !ValidColumns(map.Mappings?.Select(m => m?.Target!).ToArray())) context.AddFailure("Valid column mappings are required.");
                    break;
                case FilterRecord filter:
                    if (!ValidPredicate(filter.Criteria, 0)) context.AddFailure("Use a valid typed filter expression (maximum depth 12).");
                    break;
                case ParquetRecord parquet:
                    if (parquet.Path is null) context.AddFailure("Output path is required.");
                    break;
                case CsvOutputRecord csv:
                    if (csv.Path is null || csv.Delimiter is null) context.AddFailure("CSV output path and delimiter are required.");
                    break;
                case ParquetInputRecord parquet:
                    if (parquet.Path is null || !ValidColumns(parquet.Schema)) context.AddFailure("Parquet input path and a valid schema are required.");
                    break;
                case ExcelInputRecord excel:
                    if (excel.Path is null || excel.Sheet is null || !ValidColumns(excel.Schema)) context.AddFailure("Excel input path, sheet and a valid schema are required.");
                    break;
                case PostgresInputRecord postgres:
                    if (postgres.Host is null || postgres.Port is null || postgres.Database is null || postgres.Username is null ||
                        postgres.Password is null || postgres.SslMode is null || postgres.Query is null || !ValidColumns(postgres.Schema))
                        context.AddFailure("PostgreSQL connection properties, query and a valid schema are required.");
                    else if (string.IsNullOrWhiteSpace(postgres.Host) || string.IsNullOrWhiteSpace(postgres.Database) ||
                        string.IsNullOrWhiteSpace(postgres.Username) || string.IsNullOrWhiteSpace(postgres.Query) || postgres.Query.Length > 65536 ||
                        !HasVariable(postgres.Port) && (!int.TryParse(postgres.Port, out var port) || port is < 1 or > 65535) ||
                        !HasVariable(postgres.SslMode) && postgres.SslMode is not ("Disable" or "Prefer" or "Require" or "VerifyCA" or "VerifyFull") ||
                        !string.IsNullOrEmpty(postgres.Password) && !IsSecretReference(postgres.Password))
                        context.AddFailure("PostgreSQL connection values, SQL length, SSL mode, port and secret password are invalid.");
                    break;
                case ExcelOutputRecord excel:
                    if (excel.Path is null || excel.Sheet is null) context.AddFailure("Excel output path and sheet are required.");
                    break;
            }
        });
    }

    private static bool ValidColumns(ColumnRecord[]? columns) => columns is { Length: >= 1 and <= 200 } && columns.All(c => c is not null && c.Name is not null && Enum.IsDefined(c.Type));

    private static bool HasVariable(string? value) => value?.Contains("${", StringComparison.Ordinal) == true;

    private static bool IsSecretReference(string value) => value.StartsWith("${secret.", StringComparison.Ordinal) && value.EndsWith('}') &&
        value.Length > 10 && value[9..^1].All(character => char.IsAsciiLetterOrDigit(character) || character == '_');

    private static bool ValidPredicate(PredicateRecord? predicate, int depth)
    {
        if (depth >= 12) return false;
        return predicate switch
        {
            ComparisonRecord compare => compare.Column is not null && compare.Value is not null &&
                new[] { "equal", "notEqual", "greater", "greaterOrEqual", "less", "lessOrEqual" }.Contains(compare.Operator, StringComparer.OrdinalIgnoreCase),
            NullRecord check => check.Column is not null,
            LogicalRecord logical => logical.Children is not null &&
                ((logical.Operator == "not" && logical.Children.Length == 1) || (logical.Operator is "and" or "or" && logical.Children.Length == 2)) &&
                logical.Children.All(child => ValidPredicate(child, depth + 1)),
            _ => false
        };
    }
}
