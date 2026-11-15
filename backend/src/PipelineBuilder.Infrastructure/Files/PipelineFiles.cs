using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Parquet;
using Parquet.Schema;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Infrastructure.Files;

public sealed class PipelineFiles(ServerPaths paths) : IPipelineFiles
{
    private const int BatchSize = 1024;

    public async Task<ColumnRecord[]> Discover(string path, string delimiter, bool hasHeader, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(paths.Input(path), new UTF8Encoding(false, true));
            using var csv = new CsvReader(reader, Configuration(delimiter, hasHeader));
            if (!await csv.ReadAsync()) throw new PipelineDataException("CSV is empty.");
            var names = hasHeader ? ReadHeader(csv) : Enumerable.Range(1, csv.Parser.Count).Select(i => $"column{i}").ToArray();
            if (names.Length is < 1 or > 200 || names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                throw new PipelineDataException("CSV needs 1 to 200 nonempty, unique column names.");
            List<string?[]> sample = [];
            if (!hasHeader) sample.Add(ReadFields(csv, names.Length));
            while (sample.Count < 100 && await csv.ReadAsync())
            {
                ct.ThrowIfCancellationRequested();
                sample.Add(ReadFields(csv, names.Length));
            }
            return names.Select((name, index) => Infer(name, sample.Select(row => row[index]).ToArray())).ToArray();
        }
        catch (Exception ex) when (ex is CsvHelperException or IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw new PipelineDataException("Cannot sample CSV. Check file access, UTF-8 encoding, delimiter and record structure.", inner: ex);
        }
    }

    public async Task<ExecutionResultRecord> Execute(DefinitionRecord definition, Func<CancellationToken, Task<bool>> isCancelled, CancellationToken ct)
    {
        var pipeline = definition.MapTo();
        var analysis = PipelineValidation.Analyze(pipeline);
        if (analysis.Issues.Length > 0) throw new PipelineDataException("Run snapshot has an invalid definition.");
        var input = pipeline.Steps[0];
        var inputPathExpression = input switch { CsvStep csv => csv.Path, ParquetInputStep parquet => parquet.Path, ExcelInputStep excel => excel.Path, _ => throw new PipelineDataException("Unsupported input.") };
        var output = pipeline.Steps[^1];
        var outputPathExpression = output switch { ParquetStep parquet => parquet.Path, CsvOutputStep csv => csv.Path, ExcelOutputStep excel => excel.Path, _ => throw new PipelineDataException("Unsupported output.") };
        string? temporary = null;
        var activeBlock = input.Id;
        try
        {
            var inputPath = paths.Input(inputPathExpression);
            string outputPath;
            try { outputPath = paths.Output(outputPathExpression); }
            catch (PipelineDataException ex) { throw new PipelineDataException(ex.Message, output.Id, ex); }
            temporary = outputPath + $".{Guid.NewGuid():N}.tmp";
            var outputSchema = analysis.Schemas[^1].Input;
            long read = 0, written = 0;
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                var parquetSchema = output is ParquetStep ? new ParquetSchema(outputSchema.Select(ParquetBatchWriter.CreateField)) : null;
                await using var writer = parquetSchema is null ? null : await ParquetWriter.CreateAsync(parquetSchema, stream, cancellationToken: ct);
                using var textWriter = output is CsvOutputStep ? new StreamWriter(stream, new UTF8Encoding(false), 65536, leaveOpen: true) : null;
                using var csvOutput = output is CsvOutputStep csvStep ? new CsvWriter(textWriter!, Configuration(csvStep.Delimiter, csvStep.HasHeader)) : null;
                await using var excelWriter = output is ExcelOutputStep excelOutput
                    ? new ExcelOutputWriter(stream, outputPath, excelOutput.Sheet, output.Id) : null;
                if (output is ExcelOutputStep { HasHeader: true })
                    excelWriter!.Write(outputSchema.Select(column => (object?)column.Name));
                if (output is CsvOutputStep { HasHeader: true })
                {
                    foreach (var column in outputSchema) csvOutput!.WriteField(column.Name);
                    await csvOutput!.NextRecordAsync();
                }
                List<Dictionary<string, object?>> batch = [];
                await foreach (var inputRow in PipelineInputReader.Read(input, inputPath, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    activeBlock = input.Id;
                    if (read % BatchSize == 0 && await isCancelled(ct)) throw new OperationCanceledException("Run cancelled.");
                    read++;
                    var row = inputRow;
                    var keep = true;
                    for (var i = 1; i < pipeline.Steps.Length - 1; i++)
                    {
                        activeBlock = pipeline.Steps[i].Id;
                        switch (pipeline.Steps[i])
                        {
                            case MapStep map:
                                row = map.Mappings.ToDictionary(m => m.Target.Name, m => ConvertValue(ValueConversion.Format(row[m.Source]), m.Target, m.Format, read, map.Id), StringComparer.Ordinal);
                                break;
                            case FilterStep filter:
                                keep = filter.Criteria.Evaluate(row, analysis.Schemas[i].Input);
                                break;
                        }
                        if (!keep) break;
                    }
                    if (keep)
                    {
                        if (csvOutput is not null)
                        {
                            activeBlock = output.Id;
                            foreach (var column in outputSchema) csvOutput.WriteField(ValueConversion.Format(row[column.Name]));
                            await csvOutput.NextRecordAsync();
                        }
                        else if (excelWriter is not null)
                        {
                            activeBlock = output.Id;
                            excelWriter.Write(outputSchema.Select(column => row[column.Name]));
                        }
                        else batch.Add(row);
                        written++;
                    }
                    if (batch.Count == BatchSize)
                    {
                        activeBlock = output.Id;
                        await ParquetBatchWriter.Write(writer!, parquetSchema!, outputSchema, batch, ct);
                        batch.Clear();
                    }
                }
                activeBlock = output.Id;
                if (writer is not null && (batch.Count > 0 || written == 0)) await ParquetBatchWriter.Write(writer, parquetSchema!, outputSchema, batch, ct);
                if (excelWriter is not null) await excelWriter.Complete(ct);
            }
            ct.ThrowIfCancellationRequested();
            if (await isCancelled(ct)) throw new OperationCanceledException("Run cancelled.");
            paths.Output(outputPathExpression);
            File.Move(temporary, outputPath, overwrite: false);
            temporary = null;
            return new(read, written);
        }
        catch (PipelineDataException ex) when (ex.BlockId is null)
        {
            throw new PipelineDataException(ex.Message, activeBlock, ex);
        }
        catch (Exception ex) when (ex is CsvHelperException or IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            throw new PipelineDataException("File processing failed. Check access, CSV structure and whether the output already exists.", activeBlock, ex);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            throw new PipelineDataException("File processing failed. Check the file format, schema, sheet name and supported value types.", activeBlock, ex);
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static object? ConvertValue(string? text, Column column, string? format, long row, string blockId)
    {
        try { return ValueConversion.Convert(text, column, format); }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new PipelineDataException($"Record {row}, column '{column.Name}' cannot be converted to {column.Type}; check nullability, format, precision and scale.", blockId);
        }
    }

    private static CsvConfiguration Configuration(string delimiter, bool hasHeader) => new(CultureInfo.InvariantCulture)
    {
        Delimiter = delimiter, HasHeaderRecord = hasHeader, MaxFieldSize = 1_048_576,
        ExceptionMessagesContainRawData = false, DetectColumnCountChanges = true
    };

    private static string[] ReadHeader(CsvReader csv) { csv.ReadHeader(); return csv.HeaderRecord!; }
    private static string?[] ReadFields(CsvReader csv, int count) => Enumerable.Range(0, count).Select(csv.GetField).ToArray();

    private static ColumnRecord Infer(string name, string?[] values)
    {
        var present = values.Where(v => !string.IsNullOrEmpty(v)).ToArray();
        var nullable = present.Length != values.Length || present.Length == 0;
        foreach (var type in new[] { ColumnTypeRecord.Boolean, ColumnTypeRecord.Int64, ColumnTypeRecord.Decimal, ColumnTypeRecord.Double, ColumnTypeRecord.Date, ColumnTypeRecord.Timestamp })
        {
            if (present.Length == 0) break;
            var scale = type == ColumnTypeRecord.Decimal ? present.Select(v => decimal.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) ? (decimal.GetBits(d)[3] >> 16) & 0x7f : 0).Max() : 2;
            var column = new ColumnRecord(name, type, nullable, 28, scale);
            try
            {
                foreach (var value in present) ValueConversion.Convert(value, column.MapTo());
                return column;
            }
            catch (Exception ex) when (ex is FormatException or OverflowException) { /* Try the next conservative type. */ }
        }
        return new(name, ColumnTypeRecord.String, nullable);
    }
}
