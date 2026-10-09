namespace Hotel.Application.Ari;

public record AriCalendar(DateOnly From, DateOnly To, IReadOnlyList<AriCalendarRoomType> RoomTypes);

public record AriCalendarRoomType(Guid RoomTypeId, string Name, string Currency, IReadOnlyList<AriInventoryCell> Inventory, IReadOnlyList<AriCalendarRatePlan> RatePlans);

public record AriInventoryCell(DateOnly Date, int PhysicalRooms, int Sellable, int Booked, int Available, bool HasOverride);

public record AriCalendarRatePlan(Guid RatePlanId, string Name, IReadOnlyList<AriRateCell> Days);

public record AriRateCell(DateOnly Date, decimal Price, int MinStay, bool StopSell, bool ClosedToArrival, bool ClosedToDeparture, bool IsDefault);

public record BulkRateUpdate(
    IReadOnlyList<Guid> RatePlanIds,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DayOfWeek>? Weekdays,
    decimal? Price,
    int? MinStay,
    bool? StopSell,
    bool? ClosedToArrival,
    bool? ClosedToDeparture);

public record InventoryUpdate(Guid RoomTypeId, DateOnly From, DateOnly To, int? MaxSellable);

/// <summary>Channel-agnostic ARI payload. Connectors translate it to their wire format.</summary>
public record AriSnapshot(IReadOnlyList<AriRoomDay> Availability, IReadOnlyList<AriRateDay> Rates);

public record AriRoomDay(Guid RoomTypeId, DateOnly Date, int Available);

public record AriRateDay(Guid RoomTypeId, Guid RatePlanId, DateOnly Date, decimal Price, string Currency, int MinStay,
    bool StopSell, bool ClosedToArrival, bool ClosedToDeparture);
