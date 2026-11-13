using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.UseCases.Files;

public sealed record DiscoverSchemaInput(string Path, string Delimiter, bool HasHeader) : IInput;
public sealed record DiscoverSchemaOutput(ColumnRecord[] Schema, int SampleLimit);
public sealed class DiscoverSchemaQuery(IPipelineFiles files) : IUseCase<DiscoverSchemaInput, DiscoverSchemaOutput>
{
    public async Task<ErrorOr<DiscoverSchemaOutput>> Handle(DiscoverSchemaInput input, CancellationToken ct)
    {
        try { return new DiscoverSchemaOutput(await files.Discover(input.Path, input.Delimiter, input.HasHeader, ct), 100); }
        catch (PipelineDataException ex) { return Error.Validation("Csv.Discovery", ex.Message); }
    }
}
public sealed class DiscoverSchemaValidator : AbstractValidator<DiscoverSchemaInput>
{
    public DiscoverSchemaValidator()
    {
        RuleFor(x => x.Path).NotEmpty().MaximumLength(4096);
        RuleFor(x => x.Delimiter).NotNull().Length(1).Must(d => d is not "\r" and not "\n" and not "\"");
    }
}
