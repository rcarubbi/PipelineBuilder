using System.Globalization;

namespace PipelineBuilder.Domain.Pipelines;

public static class ValueConversion
{
    public static object? Convert(string? value, Column column, string? format = null)
    {
        if (value is null || value.Length == 0 && column.Type != ColumnType.String)
        {
            if (!column.Nullable) throw new FormatException("A required value is missing.");
            return null;
        }
        return column.Type switch
        {
            ColumnType.String => value,
            ColumnType.Boolean => bool.Parse(value),
            ColumnType.Int64 => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            ColumnType.Decimal => ParseDecimal(value, column),
            ColumnType.Double => ParseDouble(value),
            ColumnType.Date => DateOnly.ParseExact(value, format ?? "yyyy-MM-dd", CultureInfo.InvariantCulture),
            ColumnType.Timestamp => DateTimeOffset.ParseExact(value, format ?? "O", CultureInfo.InvariantCulture, DateTimeStyles.None).UtcDateTime,
            _ => throw new FormatException("Unsupported column type.")
        };
    }

    public static string? Format(object? value) => value switch
    {
        null => null,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static double ParseDouble(string value)
    {
        var result = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        return double.IsFinite(result) ? result : throw new FormatException("Non-finite numbers are not supported.");
    }

    private static decimal ParseDecimal(string value, Column column)
    {
        var parts = value.TrimStart('+', '-').Split('.');
        if (parts.Length > 2 || parts[0].TrimStart('0').Length > column.Precision - column.Scale ||
            parts.Length == 2 && parts[1].TrimEnd('0').Length > column.Scale)
            throw new FormatException("Value does not fit the declared decimal precision and scale.");
        var result = decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        var limit = 1m;
        for (var i = 0; i < column.Precision - column.Scale; i++) limit *= 10;
        if (result <= -limit || result >= limit || decimal.Round(result, column.Scale) != result)
            throw new FormatException("Value does not fit the declared decimal precision and scale.");
        return result;
    }
}
