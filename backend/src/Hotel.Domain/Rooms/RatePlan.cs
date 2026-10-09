using Hotel.Domain.Common;

namespace Hotel.Domain.Rooms;

public enum MealPlan { RoomOnly = 0, BedAndBreakfast = 1, HalfBoard = 2, FullBoard = 3, AllInclusive = 4 }

public class RatePlan : Entity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public Guid RoomTypeId { get; set; }
    public RoomType? RoomType { get; set; }
    public MealPlan MealPlan { get; set; }
    public bool IsRefundable { get; set; } = true;
    /// <summary>Free cancellation until N days before arrival. Null = non-refundable.</summary>
    public int? FreeCancellationDays { get; set; } = 1;
    /// <summary>Multiplier applied to BasePrice when no explicit DailyRate exists (e.g. 0.9 for non-refundable).</summary>
    public decimal PriceFactor { get; set; } = 1m;
    public bool IsActive { get; set; } = true;
}
