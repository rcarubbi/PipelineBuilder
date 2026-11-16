using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Application.UseCases.Files;

public sealed record DiscoverSchemaInput(string Path, string Delimiter, bool HasHeader, string Format = "csv", string? Sheet = null,
    ApiInputRecord? Api = null, PostgresInputRecord? Postgres = null) : IInput;
public sealed record DiscoverSchemaOutput(ColumnRecord[] Schema, int SampleLimit, string[]? DataPaths = null);
public sealed class DiscoverSchemaQuery(IPipelineFiles files) : IUseCase<DiscoverSchemaInput, DiscoverSchemaOutput>
{
    public async Task<ErrorOr<DiscoverSchemaOutput>> Handle(DiscoverSchemaInput input, CancellationToken ct)
    {
        try
        {
            ColumnRecord[] schema;
            string[]? dataPaths = null;
            var sampleLimit = 100;
            switch (input.Format)
            {
                case "csv": schema = await files.Discover(input.Path, input.Delimiter, input.HasHeader, ct); break;
                case "parquet": schema = await files.DiscoverParquet(input.Path, ct); break;
                case "excel" when !string.IsNullOrWhiteSpace(input.Sheet): schema = await files.DiscoverExcel(input.Path, input.Sheet, input.HasHeader, ct); break;
                case "api" when input.Api is not null:
                    var api = await files.DiscoverApi(input.Api.MapTo() as ApiInputStep ?? throw new PipelineDataException("Invalid API configuration."), ct);
                    schema = api.Schema; dataPaths = api.DataPaths; break;
                case "postgres" when input.Postgres is not null:
                    schema = await files.DiscoverPostgres(input.Postgres.MapTo() as PostgresInputStep ?? throw new PipelineDataException("Invalid PostgreSQL configuration."), ct);
                    break;
                default: throw new PipelineDataException("Choose CSV, Parquet, Excel, API, or PostgreSQL and provide any required settings.");
            }
            return new DiscoverSchemaOutput(schema, sampleLimit, dataPaths);
        }
        catch (PipelineDataException ex) { return Error.Validation("Csv.Discovery", ex.Message); }
    }
}
public sealed class DiscoverSchemaValidator : AbstractValidator<DiscoverSchemaInput>
{
    public DiscoverSchemaValidator()
    {
        RuleFor(x => x.Path).MaximumLength(4096).NotEmpty().When(x => x.Format is not ("api" or "postgres"));
        RuleFor(x => x.Format).Must(format => format is "csv" or "parquet" or "excel" or "api" or "postgres");
        RuleFor(x => x.Sheet).MaximumLength(128);
        RuleFor(x => x.Delimiter).NotNull().Length(1).Must(d => d is not "\r" and not "\n" and not "\"").When(x => x.Format == "csv");
        When(x => x.Format == "postgres", () => RuleFor(x => x.Postgres).NotNull().Must(ValidPostgres));
    }

    private static bool ValidPostgres(PostgresInputRecord? postgres) => postgres is not null &&
        !string.IsNullOrWhiteSpace(postgres.Host) && int.TryParse(postgres.Port, out var port) && port is >= 1 and <= 65535 &&
        !string.IsNullOrWhiteSpace(postgres.Database) && !string.IsNullOrWhiteSpace(postgres.Username) &&
        postgres.SslMode is "Disable" or "Prefer" or "Require" or "VerifyCA" or "VerifyFull" &&
        !string.IsNullOrWhiteSpace(postgres.Query) && postgres.Query.Length <= 65536 &&
        (string.IsNullOrEmpty(postgres.Password) || IsSecretReference(postgres.Password));

    private static bool IsSecretReference(string value) => value.StartsWith("${secret.", StringComparison.Ordinal) && value.EndsWith('}') &&
        value.Length > 10 && value[9..^1].All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
}
