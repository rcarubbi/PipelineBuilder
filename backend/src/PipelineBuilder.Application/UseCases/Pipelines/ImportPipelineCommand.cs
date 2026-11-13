using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.Validation;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Application.UseCases.Pipelines;

public sealed record ImportPipelineInput(DocumentRecord Document) : ITransactionalInput;
public sealed record ImportPipelineOutput(SavedPipelineRecord Pipeline);
/// <summary>Creates a new pipeline from an exported document; it never overwrites an existing pipeline.</summary>
public sealed class ImportPipelineCommand(IPipelineRepository repository, IDesignerQueries queries) : IUseCase<ImportPipelineInput, ImportPipelineOutput>
{
    private const string ImportedSuffix = " (imported)";
    private const int MaxNameAttempts = 100;

    public async Task<ErrorOr<ImportPipelineOutput>> Handle(ImportPipelineInput input, CancellationToken ct)
    {
        var analysis = PipelineValidation.Analyze(input.Document.Definition.MapTo());
        if (analysis.Issues.Length > 0) return analysis.Issues.Select(i => Error.Validation(i.BlockId, i.Message)).ToList();
        var name = await UniqueName(input.Document.Name.Trim(), ct);
        if (name is null) return Error.Conflict("Pipeline.Name", "Too many pipelines share this name. Rename the pipeline in the file and import again.");
        var result = await repository.Save(Guid.NewGuid(), 0, input.Document with { Name = name }, ct);
        return result.IsError ? result.Errors : new ImportPipelineOutput(result.Value);
    }

    private async Task<string?> UniqueName(string name, CancellationToken ct)
    {
        if (!await queries.PipelineNameExists(name, ct)) return name;
        var stem = name.Length > DocumentValidator.NameMaxLength - 20 ? name[..(DocumentValidator.NameMaxLength - 20)] : name;
        for (var attempt = 1; attempt <= MaxNameAttempts; attempt++)
        {
            var candidate = attempt == 1 ? stem + ImportedSuffix : $"{stem} (imported {attempt})";
            if (!await queries.PipelineNameExists(candidate, ct)) return candidate;
        }
        return null;
    }
}
public sealed class ImportPipelineValidator : AbstractValidator<ImportPipelineInput>
{
    public ImportPipelineValidator() => RuleFor(x => x.Document).NotNull().SetValidator(new DocumentValidator());
}
