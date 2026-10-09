using Microsoft.AspNetCore.Identity;

namespace Hotel.Infrastructure.Identity;

public class AppUser : IdentityUser<Guid>
{
    public string? FullName { get; set; }
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
}
