using Hotel.Application.Abstractions;
using Hotel.Application.Availability;
using Hotel.Domain.Bookings;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Application.Dashboard;

public record DashboardDto(
    DateOnly Date,
    int TotalRooms,
    int Arrivals,
    int Departures,
    int InHouse,
    int PendingBookings,
    int OverbookedBookings,
    IReadOnlyList<OccupancyPoint> Occupancy,
    IReadOnlyList<SourceRevenue> RevenueBySource,
    IReadOnlyList<ChannelHealth> Channels);

public record OccupancyPoint(DateOnly Date, int Booked, int Total, decimal Percent);

public record SourceRevenue(BookingSource Source, int Bookings, decimal Revenue, decimal Commission);

public record ChannelHealth(Guid Id, string Name, bool IsEnabled, DateTimeOffset? LastPull, DateTimeOffset? LastPush, string? LastError);

public class DashboardService(IAppDbContext db, AvailabilityService availability, TimeProvider clock)
{
    public async Task<DashboardDto> GetAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 7, 90);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var active = Booking.ActiveStatuses;

        var arrivals = await db.Bookings.CountAsync(b => b.CheckIn == today && active.Contains(b.Status), ct);
        var departures = await db.Bookings.CountAsync(b => b.CheckOut == today && (active.Contains(b.Status) || b.Status == BookingStatus.CheckedOut), ct);
        var inHouse = await db.Bookings.CountAsync(b => b.Status == BookingStatus.CheckedIn, ct);
        var pending = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Pending, ct);
        var overbooked = await db.Bookings.CountAsync(b => b.IsOverbooked && active.Contains(b.Status) && b.CheckOut >= today, ct);

        var typeIds = await db.RoomTypes.Where(r => r.IsActive).Select(r => r.Id).ToListAsync(ct);
        var inventory = await availability.GetInventoryAsync(typeIds, today, today.AddDays(days), ct);
        var occupancy = Enumerable.Range(0, days).Select(i =>
        {
            var date = today.AddDays(i);
            var cells = inventory.Values.Select(v => v[i]).ToList();
            var total = cells.Sum(c => c.PhysicalRooms);
            var booked = cells.Sum(c => c.Booked);
            return new OccupancyPoint(date, booked, total, total == 0 ? 0 : Math.Round(100m * booked / total, 1));
        }).ToList();

        var since = today.AddDays(-30);
        var revenue = (await db.Bookings.AsNoTracking()
                .Where(b => b.CheckIn >= since && b.Status != BookingStatus.Cancelled)
                .Select(b => new { b.Source, b.TotalAmount, b.CommissionAmount })
                .ToListAsync(ct))
            .GroupBy(b => b.Source)
            .Select(g => new SourceRevenue(g.Key, g.Count(), g.Sum(x => x.TotalAmount), g.Sum(x => x.CommissionAmount ?? 0)))
            .OrderByDescending(x => x.Revenue).ToList();

        var channels = await db.ChannelConnections.AsNoTracking().OrderBy(c => c.Name)
            .Select(c => new ChannelHealth(c.Id, c.Name, c.IsEnabled, c.LastReservationPullAt, c.LastAriPushAt, c.LastError))
            .ToListAsync(ct);

        return new DashboardDto(today, occupancy.FirstOrDefault()?.Total ?? 0, arrivals, departures, inHouse, pending, overbooked,
            occupancy, revenue, channels);
    }
}
