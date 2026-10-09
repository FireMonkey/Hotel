namespace Hotel.Domain.Rooms;

/// <summary>
/// ARI (Availability, Rates, Inventory) row per rate plan per night.
/// This is what gets pushed to channels.
/// </summary>
public class DailyRate
{
    public Guid RatePlanId { get; set; }
    public RatePlan? RatePlan { get; set; }
    public DateOnly Date { get; set; }
    public decimal Price { get; set; }
    public int MinStay { get; set; } = 1;
    public bool StopSell { get; set; }
    public bool ClosedToArrival { get; set; }
    public bool ClosedToDeparture { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
