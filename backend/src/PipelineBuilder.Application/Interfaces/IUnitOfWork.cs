namespace PipelineBuilder.Application.Interfaces;

public interface ITransactionalInput : IInput;
public interface IUnitOfWork
{
    Task<ICommandTransaction> Begin(CancellationToken ct);
}
public interface ICommandTransaction : IAsyncDisposable
{
    Task Commit(CancellationToken ct);
}
