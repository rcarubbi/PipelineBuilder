using System.Text.Json;
using FluentValidation;
using PipelineBuilder.Application.Contracts;

namespace PipelineBuilder.Application.Validation;

public sealed class DocumentValidator : AbstractValidator<DocumentRecord>
{
    public const int NameMaxLength = 200;

    public DocumentValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(NameMaxLength);
        RuleFor(x => x.Definition).NotNull().SetValidator(new DefinitionValidator());
        RuleFor(x => x.Environments).NotNull().Must(e => e is { Count: > 0 } && e.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value is not null && p.Value.All(v => !string.IsNullOrWhiteSpace(v.Key) && v.Value is not null)))
            .WithMessage("Provide at least one named environment of name/value variables.");
        RuleFor(x => x.Workspace).Must(w => w.ValueKind == JsonValueKind.Object).WithMessage("Workspace must be a JSON object.");
    }
}
