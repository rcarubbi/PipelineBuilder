using Microsoft.EntityFrameworkCore.Storage;
using PipelineBuilder.Application.Interfaces;

namespace PipelineBuilder.Infrastructure.Persistence;

public sealed class UnitOfWork(PipelineDbContext db) : IUnitOfWork
{
    public async Task<ICommandTransaction> Begin(CancellationToken ct) => new CommandTransaction(await db.Database.BeginTransactionAsync(ct));

    private sealed class CommandTransaction(IDbContextTransaction transaction) : ICommandTransaction
    {
        public Task Commit(CancellationToken ct) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
