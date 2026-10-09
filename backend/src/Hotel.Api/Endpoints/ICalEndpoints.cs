using System.Security.Cryptography;
using System.Text;
using Hotel.Application.Availability;
using Hotel.Domain.Channels;
using Hotel.Infrastructure.Channels.ICal;
using Hotel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Api.Endpoints;

public static class ICalEndpoints
{
    public const int ExportDays = 365;

    /// <summary>Public iCal feed: one all-day event per range of dates with no free rooms.</summary>
    public static void MapICalEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/ical/{connectionId:guid}/{roomTypeId:guid}.ics", async (Guid connectionId, Guid roomTypeId, string token,
            AppDbContext db, AvailabilityService availability, CancellationToken ct) =>
        {
            var conn = await db.ChannelConnections.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == connectionId && c.Channel == ChannelType.ICal, ct);
            if (conn is null || !conn.Settings.TryGetValue(ICalConnector.ExportTokenSetting, out var expected)
                || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(token)))
                return Results.NotFound();
            var roomType = await db.RoomTypes.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roomTypeId, ct);
            if (roomType is null) return Results.NotFound();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var inventory = await availability.GetInventoryAsync([roomTypeId], today, today.AddDays(ExportDays), ct);
            var blocked = inventory[roomTypeId].Where(d => d.Available <= 0).Select(d => (d.Date, true));
            var events = Hotel.Infrastructure.Channels.AriRanges.Compress(blocked)
                .Select(r => new ICalEvent($"{roomType.Code}-{r.From:yyyyMMdd}@hotel", r.From, r.ToInclusive.AddDays(1), "Not available", false));

            return Results.Text(ICalendar.Write("Hotel", roomType.Name, events), "text/calendar; charset=utf-8");
        }).WithTags("iCal").ExcludeFromDescription();
    }
}
