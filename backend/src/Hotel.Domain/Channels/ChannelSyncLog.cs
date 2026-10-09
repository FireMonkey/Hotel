using Hotel.Domain.Common;

namespace Hotel.Domain.Channels;

public enum SyncDirection { Push = 0, Pull = 1 }

public class ChannelSyncLog : Entity
{
    public Guid ChannelConnectionId { get; set; }
    public SyncDirection Direction { get; set; }
    public required string Operation { get; set; }
    public bool Success { get; set; }
    public int ItemCount { get; set; }
    public string? Message { get; set; }
    public long DurationMs { get; set; }
}

/// <summary>
/// Transactional outbox: written in the same transaction as the change that
/// affects availability/rates, processed by the ARI push worker.
/// </summary>
public class AriOutboxMessage
{
    public long Id { get; set; }
    public Guid RoomTypeId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}
