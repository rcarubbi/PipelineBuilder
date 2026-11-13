namespace PipelineBuilder.Domain.Pipelines;

public enum ComparisonOperator { Equal, NotEqual, Greater, GreaterOrEqual, Less, LessOrEqual }
public enum LogicalOperator { And, Or, Not }

public interface IRowPredicate
{
    bool Evaluate(IReadOnlyDictionary<string, object?> row, Column[] schema);
    void Validate(Column[] schema, List<PipelineIssue> issues, string blockId, int depth);
}

public sealed record ComparisonPredicate(string Column, ComparisonOperator Operator, string Value) : IRowPredicate
{
    public bool Evaluate(IReadOnlyDictionary<string, object?> row, Column[] schema)
    {
        var actual = row[Column];
        var expected = ValueConversion.Convert(Value, schema.Single(c => c.Name == Column));
        if (actual is null || expected is null) return false;
        var comparison = actual is string text
            ? string.CompareOrdinal(text, (string)expected)
            : ((IComparable)actual).CompareTo(expected);
        return Operator switch
        {
            ComparisonOperator.Equal => comparison == 0,
            ComparisonOperator.NotEqual => comparison != 0,
            ComparisonOperator.Greater => comparison > 0,
            ComparisonOperator.GreaterOrEqual => comparison >= 0,
            ComparisonOperator.Less => comparison < 0,
            ComparisonOperator.LessOrEqual => comparison <= 0,
            _ => false
        };
    }

    public void Validate(Column[] schema, List<PipelineIssue> issues, string blockId, int depth)
    {
        var column = schema.FirstOrDefault(c => c.Name == Column);
        if (column is null) { issues.Add(new(blockId, $"Unknown filter column '{Column}'.")); return; }
        if (!Enum.IsDefined(Operator)) issues.Add(new(blockId, "Unknown comparison operator."));
        try { ValueConversion.Convert(Value, column); }
        catch (Exception ex) when (ex is FormatException or OverflowException) { issues.Add(new(blockId, $"Filter constant is not a valid {column.Type}.")); }
    }
}

public sealed record NullPredicate(string Column, bool IsNull) : IRowPredicate
{
    public bool Evaluate(IReadOnlyDictionary<string, object?> row, Column[] schema) => (row[Column] is null) == IsNull;
    public void Validate(Column[] schema, List<PipelineIssue> issues, string blockId, int depth)
    {
        if (!schema.Any(c => c.Name == Column)) issues.Add(new(blockId, $"Unknown filter column '{Column}'."));
    }
}

public sealed record LogicalPredicate(LogicalOperator Operator, IRowPredicate[] Children) : IRowPredicate
{
    public bool Evaluate(IReadOnlyDictionary<string, object?> row, Column[] schema) => Operator switch
    {
        LogicalOperator.And => Children.All(c => c.Evaluate(row, schema)),
        LogicalOperator.Or => Children.Any(c => c.Evaluate(row, schema)),
        LogicalOperator.Not => !Children[0].Evaluate(row, schema),
        _ => false
    };

    public void Validate(Column[] schema, List<PipelineIssue> issues, string blockId, int depth)
    {
        if (depth >= 12) { issues.Add(new(blockId, "Filter expressions cannot exceed 12 levels.")); return; }
        if (!Enum.IsDefined(Operator) || Children.Length != (Operator == LogicalOperator.Not ? 1 : 2))
            issues.Add(new(blockId, "NOT needs one expression; AND/OR need two."));
        foreach (var child in Children.Take(3)) child.Validate(schema, issues, blockId, depth + 1);
    }
}
