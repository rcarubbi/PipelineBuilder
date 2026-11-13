using ErrorOr;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Application.Validation;

public sealed class TransactionDecorator<TIn, TOut>(IUseCase<TIn, TOut> inner, IUnitOfWork unitOfWork) : IUseCase<TIn, TOut> where TIn : IInput
{
    public async Task<ErrorOr<TOut>> Handle(TIn input, CancellationToken ct)
    {
        if (input is not ITransactionalInput) return await inner.Handle(input, ct);
        await using var transaction = await unitOfWork.Begin(ct);
        var result = await inner.Handle(input, ct);
        if (!result.IsError) await transaction.Commit(ct);
        return result;
    }
}
