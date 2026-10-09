using Hotel.Domain.Rooms;

namespace Hotel.Application.Availability;

public record AvailabilityQuery(DateOnly CheckIn, DateOnly CheckOut, int Adults = 2, int Children = 0, Guid? RoomTypeId = null);

public record RoomTypeAvailability(
    Guid RoomTypeId,
    string Code,
    string Name,
    string? Description,
    int MaxAdults,
    int MaxChildren,
    int SizeSquareMeters,
    IReadOnlyList<string> Amenities,
    IReadOnlyList<string> ImageUrls,
    int AvailableRooms,
    IReadOnlyList<RateQuote> Rates);

public record RateQuote(
    Guid RatePlanId,
    string Code,
    string Name,
    MealPlan MealPlan,
    bool IsRefundable,
    int? FreeCancellationDays,
    decimal TotalPrice,
    decimal AverageNightlyPrice,
    string Currency,
    IReadOnlyList<NightlyPrice> Nights);

public record NightlyPrice(DateOnly Date, decimal Price);

/// <summary>Per room type / per night inventory. Used by the admin calendar and channel ARI pushes.</summary>
public record InventoryDay(DateOnly Date, int PhysicalRooms, int Sellable, int Booked)
{
    public int Available => Math.Max(0, Sellable - Booked);
}
