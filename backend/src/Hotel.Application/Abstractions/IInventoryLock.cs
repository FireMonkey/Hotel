namespace Hotel.Application.Abstractions;

/// <summary>
/// Opens a transaction and serializes inventory-changing operations per room type,
/// so two concurrent requests (web + OTA import) cannot sell the last room twice.
/// The PostgreSQL implementation uses pg_advisory_xact_lock.
/// </summary>
public interface IInventoryLock
{
    Task<IInventoryScope> BeginAsync(IEnumerable<Guid> roomTypeIds, CancellationToken ct);
}

public interface IInventoryScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}
