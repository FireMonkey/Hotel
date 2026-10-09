using Hotel.Application.Abstractions;
using Hotel.Application.Availability;
using Hotel.Domain.Common;
using Hotel.Domain.Rooms;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Application.Ari;

public class AriService(IAppDbContext db, AvailabilityService availability)
{
    public const int MaxRangeDays = 400;

    public async Task<AriCalendar> GetCalendarAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        ValidateRange(from, to, 93);
        var roomTypes = await db.RoomTypes.AsNoTracking().Include(rt => rt.RatePlans)
            .Where(rt => rt.IsActive).OrderBy(rt => rt.SortOrder).ThenBy(rt => rt.Name).ToListAsync(ct);
        var ids = roomTypes.Select(r => r.Id).ToList();
        var snapshot = await BuildSnapshotAsync(ids, from, to, ct);
        var inventory = await availability.GetInventoryAsync(ids, from, to, ct);
        var overrides = await db.InventoryOverrides.AsNoTracking()
            .Where(o => ids.Contains(o.RoomTypeId) && o.Date >= from && o.Date < to)
            .Select(o => new { o.RoomTypeId, o.Date }).ToListAsync(ct);
        var explicitRates = (await db.DailyRates.AsNoTracking()
            .Where(r => r.Date >= from && r.Date < to).Select(r => new { r.RatePlanId, r.Date }).ToListAsync(ct))
            .Select(r => (r.RatePlanId, r.Date)).ToHashSet();

        var result = roomTypes.Select(rt => new AriCalendarRoomType(
            rt.Id, rt.Name, rt.Currency,
            inventory[rt.Id].Select(d => new AriInventoryCell(d.Date, d.PhysicalRooms, d.Sellable, d.Booked, d.Available,
                overrides.Any(o => o.RoomTypeId == rt.Id && o.Date == d.Date))).ToList(),
            rt.RatePlans.Where(rp => rp.IsActive).OrderBy(rp => rp.PriceFactor).Select(rp => new AriCalendarRatePlan(
                rp.Id, rp.Name,
                snapshot.Rates.Where(r => r.RatePlanId == rp.Id).Select(r => new AriRateCell(r.Date, r.Price, r.MinStay,
                    r.StopSell, r.ClosedToArrival, r.ClosedToDeparture, !explicitRates.Contains((rp.Id, r.Date)))).ToList()
            )).ToList())).ToList();

        return new AriCalendar(from, to, result);
    }

    public async Task<int> BulkUpdateRatesAsync(BulkRateUpdate req, CancellationToken ct)
    {
        ValidateRange(req.From, req.To, MaxRangeDays);
        if (req.Price is <= 0) throw new DomainException("invalid_price", "Price must be positive.");
        if (req.MinStay is < 1) throw new DomainException("invalid_min_stay", "Minimum stay must be at least 1.");

        var plans = await db.RatePlans.Include(rp => rp.RoomType)
            .Where(rp => req.RatePlanIds.Contains(rp.Id)).ToListAsync(ct);
        if (plans.Count == 0) throw new DomainException("rate_plan_not_found", "No rate plans selected.");

        var planIds = plans.Select(p => p.Id).ToList();
        var existing = await db.DailyRates
            .Where(r => planIds.Contains(r.RatePlanId) && r.Date >= req.From && r.Date < req.To)
            .ToDictionaryAsync(r => (r.RatePlanId, r.Date), ct);

        var weekdays = req.Weekdays is { Count: > 0 } w ? w.ToHashSet() : null;
        var changed = 0;
        foreach (var plan in plans)
        {
            for (var d = req.From; d < req.To; d = d.AddDays(1))
            {
                if (weekdays is not null && !weekdays.Contains(d.DayOfWeek)) continue;
                if (!existing.TryGetValue((plan.Id, d), out var rate))
                {
                    rate = new DailyRate
                    {
                        RatePlanId = plan.Id,
                        Date = d,
                        Price = Math.Round(plan.RoomType!.BasePrice * plan.PriceFactor, 2),
                    };
                    db.DailyRates.Add(rate);
                }
                if (req.Price is { } p) rate.Price = p;
                if (req.MinStay is { } ms) rate.MinStay = ms;
                if (req.StopSell is { } ss) rate.StopSell = ss;
                if (req.ClosedToArrival is { } cta) rate.ClosedToArrival = cta;
                if (req.ClosedToDeparture is { } ctd) rate.ClosedToDeparture = ctd;
                rate.UpdatedAt = DateTimeOffset.UtcNow;
                changed++;
            }
        }
        foreach (var roomTypeId in plans.Select(p => p.RoomTypeId).Distinct())
            AriOutbox.Enqueue(db, roomTypeId, req.From, req.To);
        await db.SaveChangesAsync(ct);
        return changed;
    }

    public async Task SetInventoryAsync(InventoryUpdate req, CancellationToken ct)
    {
        ValidateRange(req.From, req.To, MaxRangeDays);
        if (req.MaxSellable is < 0) throw new DomainException("invalid_inventory", "Inventory cannot be negative.");
        var existing = await db.InventoryOverrides
            .Where(o => o.RoomTypeId == req.RoomTypeId && o.Date >= req.From && o.Date < req.To)
            .ToDictionaryAsync(o => o.Date, ct);
        for (var d = req.From; d < req.To; d = d.AddDays(1))
        {
            existing.TryGetValue(d, out var row);
            if (req.MaxSellable is null)
            {
                if (row is not null) db.InventoryOverrides.Remove(row);
            }
            else if (row is null)
                db.InventoryOverrides.Add(new InventoryOverride { RoomTypeId = req.RoomTypeId, Date = d, MaxSellable = req.MaxSellable.Value });
            else
                row.MaxSellable = req.MaxSellable.Value;
        }
        AriOutbox.Enqueue(db, req.RoomTypeId, req.From, req.To);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Effective availability and rates for [from, to), defaults applied.</summary>
    public async Task<AriSnapshot> BuildSnapshotAsync(IReadOnlyCollection<Guid> roomTypeIds, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var inventory = await availability.GetInventoryAsync(roomTypeIds, from, to, ct);
        var plans = await db.RatePlans.AsNoTracking().Include(rp => rp.RoomType)
            .Where(rp => roomTypeIds.Contains(rp.RoomTypeId) && rp.IsActive).ToListAsync(ct);
        var planIds = plans.Select(p => p.Id).ToList();
        var rates = await db.DailyRates.AsNoTracking()
            .Where(r => planIds.Contains(r.RatePlanId) && r.Date >= from && r.Date < to)
            .ToDictionaryAsync(r => (r.RatePlanId, r.Date), ct);

        var avail = inventory.SelectMany(kv => kv.Value.Select(d => new AriRoomDay(kv.Key, d.Date, d.Available))).ToList();
        var rateDays = new List<AriRateDay>();
        foreach (var p in plans)
        {
            var inactive = !p.RoomType!.IsActive;
            for (var d = from; d < to; d = d.AddDays(1))
            {
                rates.TryGetValue((p.Id, d), out var r);
                rateDays.Add(new AriRateDay(p.RoomTypeId, p.Id, d,
                    r?.Price ?? Math.Round(p.RoomType.BasePrice * p.PriceFactor, 2), p.RoomType.Currency,
                    r?.MinStay ?? 1, inactive || (r?.StopSell ?? false), r?.ClosedToArrival ?? false, r?.ClosedToDeparture ?? false));
            }
        }
        return new AriSnapshot(avail, rateDays);
    }

    private static void ValidateRange(DateOnly from, DateOnly to, int maxDays)
    {
        if (to <= from) throw new DomainException("invalid_range", "'to' must be after 'from'.");
        if (to.DayNumber - from.DayNumber > maxDays)
            throw new DomainException("invalid_range", $"Range cannot exceed {maxDays} days.");
    }
}
