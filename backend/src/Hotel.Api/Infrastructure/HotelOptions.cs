namespace Hotel.Api.Infrastructure;

/// <summary>Public hotel information shown on the website (Hotel section in appsettings).</summary>
public class HotelOptions
{
    public const string Section = "Hotel";
    public string Name { get; set; } = "Hotel";
    public string? Tagline { get; set; }
    public string? Description { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string CheckInTime { get; set; } = "14:00";
    public string CheckOutTime { get; set; } = "12:00";
    public string Currency { get; set; } = "TRY";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public List<string> ImageUrls { get; set; } = [];
    public List<string> Facilities { get; set; } = [];
    /// <summary>Direct bookings are confirmed immediately (pay at hotel). Set false to review them first.</summary>
    public bool AutoConfirmDirectBookings { get; set; } = true;
    /// <summary>Public base URL of the API, used for iCal export links.</summary>
    public string? PublicApiUrl { get; set; }
}
