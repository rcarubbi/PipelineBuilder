using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.Validation;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Application.UseCases.Pipelines;

public sealed record SavePipelineInput(Guid Id, int ExpectedRevision, DocumentRecord Document) : ITransactionalInput;
public sealed record SavePipelineOutput(SavedPipelineRecord Pipeline);
public sealed class SavePipelineCommand(IPipelineRepository repository) : IUseCase<SavePipelineInput, SavePipelineOutput>
{
    public async Task<ErrorOr<SavePipelineOutput>> Handle(SavePipelineInput input, CancellationToken ct)
    {
        var analysis = PipelineValidation.Analyze(input.Document.Definition.MapTo());
        if (analysis.Issues.Length > 0) return analysis.Issues.Select(i => Error.Validation(i.BlockId, i.Message)).ToList();
        var result = await repository.Save(input.Id, input.ExpectedRevision, input.Document, ct);
        return result.IsError ? result.Errors : new SavePipelineOutput(result.Value);
    }
}
public sealed class SavePipelineValidator : AbstractValidator<SavePipelineInput>
{
    public SavePipelineValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ExpectedRevision).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Document).NotNull().SetValidator(new DocumentValidator());
    }
}
