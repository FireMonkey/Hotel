using Hotel.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Hotel.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL: transaction + pg_advisory_xact_lock per room type (released on commit/rollback).
/// Other providers (SQLite in tests): transaction + process-wide semaphore.
/// </summary>
public class InventoryLock(AppDbContext db) : IInventoryLock
{
    private static readonly SemaphoreSlim LocalLock = new(1, 1);

    public async Task<IInventoryScope> BeginAsync(IEnumerable<Guid> roomTypeIds, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            return NoopScope.Instance; // already inside a scope owned by the caller

        if (db.Database.IsNpgsql())
        {
            var tx = await db.Database.BeginTransactionAsync(ct);
            // Sorted to avoid deadlocks when several room types are locked.
            foreach (var id in roomTypeIds.Distinct().Order())
            {
                var key = BitConverter.ToInt64(id.ToByteArray(), 0);
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", ct);
            }
            return new Scope(tx, null);
        }

        await LocalLock.WaitAsync(ct);
        try
        {
            return new Scope(await db.Database.BeginTransactionAsync(ct), LocalLock);
        }
        catch
        {
            LocalLock.Release();
            throw;
        }
    }

    private sealed class Scope(IDbContextTransaction tx, SemaphoreSlim? semaphore) : IInventoryScope
    {
        public Task CommitAsync(CancellationToken ct) => tx.CommitAsync(ct);

        public async ValueTask DisposeAsync()
        {
            await tx.DisposeAsync();
            semaphore?.Release();
        }
    }

    private sealed class NoopScope : IInventoryScope
    {
        public static readonly NoopScope Instance = new();
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
