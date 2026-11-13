using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Contracts;
using PipelineBuilder.Application.Interfaces;
using PipelineBuilder.Application.Validation;
using PipelineBuilder.Domain.Pipelines;

namespace PipelineBuilder.Application.UseCases.Pipelines;

public sealed record ValidatePipelineInput(DefinitionRecord Definition) : IInput;
public sealed record ValidatePipelineOutput(AnalysisRecord Analysis);
public sealed class ValidatePipelineQuery : IUseCase<ValidatePipelineInput, ValidatePipelineOutput>
{
    public Task<ErrorOr<ValidatePipelineOutput>> Handle(ValidatePipelineInput input, CancellationToken ct) =>
        Task.FromResult<ErrorOr<ValidatePipelineOutput>>(new ValidatePipelineOutput(PipelineValidation.Analyze(input.Definition.MapTo()).MapTo()));
}
public sealed class ValidatePipelineValidator : AbstractValidator<ValidatePipelineInput>
{
    public ValidatePipelineValidator() => RuleFor(x => x.Definition).NotNull().SetValidator(new DefinitionValidator());
}
