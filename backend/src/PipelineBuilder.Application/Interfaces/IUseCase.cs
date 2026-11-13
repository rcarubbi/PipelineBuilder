using ErrorOr;

namespace PipelineBuilder.Application.Interfaces;

public interface IInput;
public interface IUseCase<in TIn, TOut> where TIn : IInput
{
    Task<ErrorOr<TOut>> Handle(TIn input, CancellationToken ct);
}
