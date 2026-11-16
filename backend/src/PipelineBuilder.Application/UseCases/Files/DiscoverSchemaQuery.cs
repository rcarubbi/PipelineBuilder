using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Application.UseCases.Files;

public sealed record DiscoverSchemaInput(string Path, string Delimiter, bool HasHeader, string Format = "csv", string? Sheet = null, ApiInputRecord? Api = null) : IInput;
public sealed record DiscoverSchemaOutput(ColumnRecord[] Schema, int SampleLimit, string[]? DataPaths = null);
public sealed class DiscoverSchemaQuery(IPipelineFiles files) : IUseCase<DiscoverSchemaInput, DiscoverSchemaOutput>
{
    public async Task<ErrorOr<DiscoverSchemaOutput>> Handle(DiscoverSchemaInput input, CancellationToken ct)
    {
        try
        {
            var schema = input.Format switch
            {
                "csv" => new ApiSchemaDiscoveryRecord(await files.Discover(input.Path, input.Delimiter, input.HasHeader, ct), []),
                "parquet" => new ApiSchemaDiscoveryRecord(await files.DiscoverParquet(input.Path, ct), []),
                "excel" when !string.IsNullOrWhiteSpace(input.Sheet) => new ApiSchemaDiscoveryRecord(await files.DiscoverExcel(input.Path, input.Sheet, input.HasHeader, ct), []),
                "api" when input.Api is not null => await files.DiscoverApi(input.Api.MapTo() as ApiInputStep ?? throw new PipelineDataException("Invalid API configuration."), ct),
                _ => throw new PipelineDataException("Choose CSV, Parquet, or Excel and provide an Excel sheet name when needed.")
            };
            return new DiscoverSchemaOutput(schema.Schema, 100, schema.DataPaths);
        }
        catch (PipelineDataException ex) { return Error.Validation("Csv.Discovery", ex.Message); }
    }
}
public sealed class DiscoverSchemaValidator : AbstractValidator<DiscoverSchemaInput>
{
    public DiscoverSchemaValidator()
    {
        RuleFor(x => x.Path).MaximumLength(4096).NotEmpty().When(x => x.Format != "api");
        RuleFor(x => x.Format).Must(format => format is "csv" or "parquet" or "excel" or "api");
        RuleFor(x => x.Sheet).MaximumLength(128);
        RuleFor(x => x.Delimiter).NotNull().Length(1).Must(d => d is not "\r" and not "\n" and not "\"");
    }
}
