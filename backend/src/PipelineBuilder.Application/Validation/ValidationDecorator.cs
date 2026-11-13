using ErrorOr;
using FluentValidation;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.Validation;

public sealed class ValidationDecorator<TIn, TOut>(IUseCase<TIn, TOut> inner, IEnumerable<IValidator<TIn>> validators) : IUseCase<TIn, TOut> where TIn : IInput
{
    public async Task<ErrorOr<TOut>> Handle(TIn input, CancellationToken ct)
    {
        List<Error> errors = [];
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(input, ct);
            errors.AddRange(result.Errors.Select(f => Error.Validation(f.PropertyName, f.ErrorMessage)));
        }
        return errors.Count > 0 ? errors : await inner.Handle(input, ct);
    }
}
