using Parquet;
using Parquet.Schema;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Infrastructure.Files;

internal static class ParquetBatchWriter
{
    public static DataField CreateField(Column column) => column.Type switch
    {
        ColumnType.String => new DataField<string>(column.Name, column.Nullable),
        ColumnType.Boolean => new DataField<bool>(column.Name, column.Nullable),
        ColumnType.Int64 => new DataField<long>(column.Name, column.Nullable),
        ColumnType.Decimal => new DecimalDataField(column.Name, column.Precision, column.Scale, isNullable: column.Nullable),
        ColumnType.Double => new DataField<double>(column.Name, column.Nullable),
        ColumnType.Date => new DateTimeDataField(column.Name, DateTimeFormat.Date, isAdjustedToUTC: false, isNullable: column.Nullable),
        ColumnType.Timestamp => new DateTimeDataField(column.Name, DateTimeFormat.Timestamp, isAdjustedToUTC: true, unit: DateTimeTimeUnit.Micros, isNullable: column.Nullable),
        _ => throw new ArgumentException("Unsupported column type.")
    };

    public static async Task Write(ParquetWriter writer, ParquetSchema parquetSchema, Column[] schema, List<Dictionary<string, object?>> rows, CancellationToken ct)
    {
        using var group = writer.CreateRowGroup();
        foreach (var column in schema)
        {
            var field = parquetSchema.DataFields.Single(f => f.Name == column.Name);
            var values = rows.Select(r => r[column.Name]).ToArray();
            switch (column.Type)
            {
                case ColumnType.String: await group.WriteAsync(field, values.Cast<string?>().ToArray()); break;
                case ColumnType.Boolean: await WriteValues<bool>(group, field, values, column.Nullable, ct); break;
                case ColumnType.Int64: await WriteValues<long>(group, field, values, column.Nullable, ct); break;
                case ColumnType.Decimal: await WriteValues<decimal>(group, field, values, column.Nullable, ct); break;
                case ColumnType.Double: await WriteValues<double>(group, field, values, column.Nullable, ct); break;
                case ColumnType.Date:
                    await WriteValues<DateTime>(group, field, values.Select(v => v is DateOnly date ? (object)date.ToDateTime(TimeOnly.MinValue) : null).ToArray(), column.Nullable, ct);
                    break;
                case ColumnType.Timestamp: await WriteValues<DateTime>(group, field, values, column.Nullable, ct); break;
            }
            ct.ThrowIfCancellationRequested();
        }
    }

    private static Task WriteValues<T>(ParquetRowGroupWriter group, DataField field, object?[] values, bool nullable, CancellationToken ct) where T : struct => nullable
        ? group.WriteAsync<T>(field, values.Select(v => v is null ? (T?)null : (T)v).ToArray(), cancellationToken: ct)
        : group.WriteAsync<T>(field, values.Cast<T>().ToArray(), cancellationToken: ct);
}
