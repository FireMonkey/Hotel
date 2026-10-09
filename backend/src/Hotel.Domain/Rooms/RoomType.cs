using Hotel.Domain.Common;

namespace Hotel.Domain.Rooms;

public class RoomType : Entity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int MaxAdults { get; set; } = 2;
    public int MaxChildren { get; set; }
    public int SizeSquareMeters { get; set; }
    /// <summary>Fallback nightly price when no DailyRate exists for a date.</summary>
    public decimal BasePrice { get; set; }
    public string Currency { get; set; } = "TRY";
    public List<string> Amenities { get; set; } = [];
    public List<string> ImageUrls { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public List<Room> Rooms { get; set; } = [];
    public List<RatePlan> RatePlans { get; set; } = [];
}
