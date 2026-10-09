using Hotel.Application.Bookings;
using Hotel.Application.Channels;
using Hotel.Domain.Bookings;
using Hotel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hotel.Tests;

public class ChannelSyncTests(TestApp app) : IClassFixture<TestApp>
{
    private static DateOnly Day(int offset) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(offset);

    private static ExternalReservation Res(string id, ExternalReservationStatus status, int from, int to, string room = "R1") =>
        new(id, status, room, "RATE1", Day(from), Day(to), 2, 0,
            new GuestDto("John", "Smith", "john@example.com", null, "GB"), 2000m, "EUR", AckToken: "rev-" + id);

    private async Task<PullResult> Pull(Guid connectionId)
    {
        await using var scope = app.Scope();
        return await scope.ServiceProvider.GetRequiredService<ChannelSyncService>().PullReservationsAsync(connectionId, default);
    }

    private async Task<Booking?> Find(Guid connectionId, string externalId)
    {
        await using var scope = app.Scope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.ChannelConnectionId == connectionId && b.ExternalReference == externalId);
    }

    [Fact]
    public async Task Imports_modifies_and_cancels_ota_reservations()
    {
        var (type, plan) = await app.SeedRoomTypeAsync(rooms: 2);
        var conn = await app.SeedChannelAsync(type.Id, plan.Id, commission: 15);
        var id = "BDC-" + Guid.NewGuid().ToString("N")[..8];

        app.Fake.Inbox.Enqueue(Res(id, ExternalReservationStatus.New, 10, 12));
        var r1 = await Pull(conn.Id);
        Assert.Equal(1, r1.Created);
        var booking = await Find(conn.Id, id);
        Assert.NotNull(booking);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(300m, booking.CommissionAmount); // 15 % of 2000
        Assert.Equal(type.Id, booking.RoomTypeId);
        Assert.Contains(app.Fake.Acknowledged, a => a.ExternalId == id);

        // Same reservation delivered twice must not duplicate.
        app.Fake.Inbox.Enqueue(Res(id, ExternalReservationStatus.New, 10, 12));
        app.Fake.Inbox.Enqueue(Res(id, ExternalReservationStatus.Modified, 11, 14));
        await Pull(conn.Id);
        booking = await Find(conn.Id, id);
        Assert.Equal(Day(11), booking!.CheckIn);
        Assert.Equal(Day(14), booking.CheckOut);

        app.Fake.Inbox.Enqueue(Res(id, ExternalReservationStatus.Cancelled, 11, 14));
        var r3 = await Pull(conn.Id);
        Assert.Equal(1, r3.Cancelled);
        Assert.Equal(BookingStatus.Cancelled, (await Find(conn.Id, id))!.Status);
    }

    [Fact]
    public async Task Ota_reservation_without_inventory_is_accepted_and_flagged_overbooked()
    {
        var (type, plan) = await app.SeedRoomTypeAsync(rooms: 1);
        var conn = await app.SeedChannelAsync(type.Id, plan.Id);
        app.Fake.Inbox.Enqueue(Res("A-" + conn.Id, ExternalReservationStatus.New, 20, 22));
        app.Fake.Inbox.Enqueue(Res("B-" + conn.Id, ExternalReservationStatus.New, 21, 23));
        await Pull(conn.Id);

        Assert.False((await Find(conn.Id, "A-" + conn.Id))!.IsOverbooked);
        Assert.True((await Find(conn.Id, "B-" + conn.Id))!.IsOverbooked);
    }

    [Fact]
    public async Task Unmapped_room_is_not_acknowledged_so_channel_redelivers()
    {
        var (type, plan) = await app.SeedRoomTypeAsync(rooms: 1);
        var conn = await app.SeedChannelAsync(type.Id, plan.Id);
        var id = "UNMAPPED-" + conn.Id;
        app.Fake.Inbox.Enqueue(Res(id, ExternalReservationStatus.New, 5, 6, room: "UNKNOWN"));

        var result = await Pull(conn.Id);

        Assert.Single(result.Errors);
        Assert.DoesNotContain(app.Fake.Acknowledged, a => a.ExternalId == id);
        Assert.Null(await Find(conn.Id, id));
    }

    [Fact]
    public async Task Outbox_pushes_availability_and_marked_up_rates()
    {
        var (type, plan) = await app.SeedRoomTypeAsync(rooms: 3, price: 1000m);
        await app.SeedChannelAsync(type.Id, plan.Id, markup: 10);

        await using (var scope = app.Scope())
        {
            var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
            await bookings.CreateAsync(new CreateBookingRequest(plan.Id, Day(3), Day(4), 2, 0,
                new GuestDto("A", "B", "a@b.com", null, null), null), BookingSource.Direct, true, default);
            await scope.ServiceProvider.GetRequiredService<ChannelSyncService>().ProcessAriOutboxAsync(default);
        }

        var snapshot = app.Fake.Pushed.Last(s => s.Availability.Any(a => a.RoomTypeId == type.Id));
        Assert.Equal(2, snapshot.Availability.Single(a => a.RoomTypeId == type.Id && a.Date == Day(3)).Available);
        Assert.Equal(1100m, snapshot.Rates.First(r => r.RatePlanId == plan.Id).Price);

        await using var verify = app.Scope();
        Assert.False(await verify.ServiceProvider.GetRequiredService<AppDbContext>().AriOutbox.AnyAsync(m => m.ProcessedAt == null));
    }
}
