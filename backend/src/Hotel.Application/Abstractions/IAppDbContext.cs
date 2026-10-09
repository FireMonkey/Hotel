using Hotel.Domain.Bookings;
using Hotel.Domain.Channels;
using Hotel.Domain.Rooms;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<RoomType> RoomTypes { get; }
    DbSet<Room> Rooms { get; }
    DbSet<RatePlan> RatePlans { get; }
    DbSet<DailyRate> DailyRates { get; }
    DbSet<InventoryOverride> InventoryOverrides { get; }
    DbSet<Booking> Bookings { get; }
    DbSet<ChannelConnection> ChannelConnections { get; }
    DbSet<ChannelMapping> ChannelMappings { get; }
    DbSet<ChannelSyncLog> ChannelSyncLogs { get; }
    DbSet<AriOutboxMessage> AriOutbox { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
