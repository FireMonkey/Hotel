namespace Hotel.Domain.Bookings;

public class Guest
{
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
    public string? Country { get; set; }
    public string FullName => $"{FirstName} {LastName}";
}
