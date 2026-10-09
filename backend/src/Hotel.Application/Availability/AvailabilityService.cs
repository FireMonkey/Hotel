using Hotel.Application.Abstractions;
using Hotel.Domain.Bookings;
using Hotel.Domain.Common;
using Hotel.Domain.Rooms;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Application.Availability;

public class AvailabilityService(IAppDbContext db, TimeProvider clock)
{
    public const int MaxNights = 30;
    public const int MaxBookingWindowDays = 730;

    public async Task<IReadOnlyList<RoomTypeAvailability>> SearchAsync(AvailabilityQuery q, CancellationToken ct)
    {
        var stay = Validate(q.CheckIn, q.CheckOut);

        var roomTypes = await db.RoomTypes.AsNoTracking()
            .Include(rt => rt.RatePlans.Where(rp => rp.IsActive))
            .Where(rt => rt.IsActive && (q.RoomTypeId == null || rt.Id == q.RoomTypeId))
            .Where(rt => rt.MaxAdults >= q.Adults && rt.MaxAdults + rt.MaxChildren >= q.Adults + q.Children)
            .OrderBy(rt => rt.SortOrder).ThenBy(rt => rt.Name)
            .ToListAsync(ct);

        if (roomTypes.Count == 0) return [];

        var ids = roomTypes.Select(rt => rt.Id).ToList();
        var inventory = await GetInventoryAsync(ids, stay.CheckIn, stay.CheckOut, ct);
        var ratePlanIds = roomTypes.SelectMany(rt => rt.RatePlans).Select(rp => rp.Id).ToList();
        var rates = await db.DailyRates.AsNoTracking()
            .Where(r => ratePlanIds.Contains(r.RatePlanId) && r.Date >= stay.CheckIn && r.Date <= stay.CheckOut)
            .ToListAsync(ct);
        var rateLookup = rates.ToDictionary(r => (r.RatePlanId, r.Date));

        var result = new List<RoomTypeAvailability>();
        foreach (var rt in roomTypes)
        {
            var available = inventory[rt.Id].Min(d => d.Available);
            if (available <= 0) continue;

            var quotes = rt.RatePlans
                .OrderBy(rp => rp.PriceFactor)
                .Select(rp => Quote(rt, rp, stay, rateLookup))
                .OfType<RateQuote>()
                .ToList();

            if (quotes.Count == 0) continue;

            result.Add(new RoomTypeAvailability(rt.Id, rt.Code, rt.Name, rt.Description, rt.MaxAdults, rt.MaxChildren,
                rt.SizeSquareMeters, rt.Amenities, rt.ImageUrls, available, quotes));
        }
        return result;
    }

    /// <summary>Price a specific rate plan for a stay. Returns null when restrictions forbid selling it.</summary>
    public async Task<RateQuote?> QuoteAsync(Guid ratePlanId, DateOnly checkIn, DateOnly checkOut, CancellationToken ct)
    {
        var stay = new StayPeriod(checkIn, checkOut);
        var rp = await db.RatePlans.AsNoTracking().Include(x => x.RoomType)
            .FirstOrDefaultAsync(x => x.Id == ratePlanId, ct)
            ?? throw new DomainException("rate_plan_not_found", "Rate plan not found.");
        var rates = await db.DailyRates.AsNoTracking()
            .Where(r => r.RatePlanId == ratePlanId && r.Date >= stay.CheckIn && r.Date <= stay.CheckOut)
            .ToDictionaryAsync(r => (r.RatePlanId, r.Date), ct);
        return Quote(rp.RoomType!, rp, stay, rates);
    }

    /// <summary>
    /// Inventory per night for the given room types in [from, to). Bookings in an active status
    /// consume inventory; rooms out of order are excluded; InventoryOverride caps sellable rooms.
    /// </summary>
    public async Task<Dictionary<Guid, List<InventoryDay>>> GetInventoryAsync(
        IReadOnlyCollection<Guid> roomTypeIds, DateOnly from, DateOnly to, CancellationToken ct, Guid? excludeBookingId = null)
    {
        var physical = await db.Rooms.AsNoTracking()
            .Where(r => roomTypeIds.Contains(r.RoomTypeId) && r.Status == RoomStatus.Available)
            .GroupBy(r => r.RoomTypeId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var overrides = await db.InventoryOverrides.AsNoTracking()
            .Where(o => roomTypeIds.Contains(o.RoomTypeId) && o.Date >= from && o.Date < to)
            .ToDictionaryAsync(o => (o.RoomTypeId, o.Date), o => o.MaxSellable, ct);

        var active = Booking.ActiveStatuses;
        var bookings = await db.Bookings.AsNoTracking()
            .Where(b => roomTypeIds.Contains(b.RoomTypeId) && active.Contains(b.Status)
                        && b.CheckIn < to && from < b.CheckOut
                        && (excludeBookingId == null || b.Id != excludeBookingId))
            .Select(b => new { b.RoomTypeId, b.CheckIn, b.CheckOut })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, List<InventoryDay>>();
        foreach (var id in roomTypeIds)
        {
            var rooms = physical.GetValueOrDefault(id);
            var days = new List<InventoryDay>();
            for (var d = from; d < to; d = d.AddDays(1))
            {
                var date = d;
                var booked = bookings.Count(b => b.RoomTypeId == id && b.CheckIn <= date && date < b.CheckOut);
                var sellable = overrides.TryGetValue((id, date), out var cap) ? Math.Min(cap, rooms) : rooms;
                days.Add(new InventoryDay(date, rooms, sellable, booked));
            }
            result[id] = days;
        }
        return result;
    }

    private StayPeriod Validate(DateOnly checkIn, DateOnly checkOut)
    {
        var stay = new StayPeriod(checkIn, checkOut);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (stay.CheckIn < today.AddDays(-1)) // -1: tolerate guests in time zones behind UTC
            throw new DomainException("invalid_stay", "Check-in cannot be in the past.");
        if (stay.Nights > MaxNights)
            throw new DomainException("invalid_stay", $"Maximum stay is {MaxNights} nights.");
        if (stay.CheckIn > today.AddDays(MaxBookingWindowDays))
            throw new DomainException("invalid_stay", "Check-in is too far in the future.");
        return stay;
    }

    internal static RateQuote? Quote(RoomType rt, RatePlan rp, StayPeriod stay, IReadOnlyDictionary<(Guid, DateOnly), DailyRate> rates)
    {
        var nights = new List<NightlyPrice>(stay.Nights);
        foreach (var date in stay.Dates())
        {
            rates.TryGetValue((rp.Id, date), out var rate);
            if (rate is not null)
            {
                if (rate.StopSell) return null;
                if (date == stay.CheckIn && (rate.ClosedToArrival || rate.MinStay > stay.Nights)) return null;
            }
            var price = rate?.Price ?? Math.Round(rt.BasePrice * rp.PriceFactor, 2);
            if (price <= 0) return null;
            nights.Add(new NightlyPrice(date, price));
        }
        if (rates.TryGetValue((rp.Id, stay.CheckOut), out var departure) && departure.ClosedToDeparture)
            return null;

        var total = nights.Sum(n => n.Price);
        return new RateQuote(rp.Id, rp.Code, rp.Name, rp.MealPlan, rp.IsRefundable, rp.FreeCancellationDays,
            total, Math.Round(total / stay.Nights, 2), rt.Currency, nights);
    }
}
