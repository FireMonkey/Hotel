using Hotel.Domain.Rooms;
using Hotel.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hotel.Infrastructure.Persistence;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config, bool sampleData, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbSeeder));

        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { Roles.Admin, Roles.Staff })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));

        var email = config["Admin:Email"];
        var password = config["Admin:Password"];
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
        {
            var users = services.GetRequiredService<UserManager<AppUser>>();
            if (await users.FindByEmailAsync(email) is null)
            {
                var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = "Administrator" };
                var result = await users.CreateAsync(user, password);
                if (!result.Succeeded)
                    throw new InvalidOperationException("Admin seed failed: " + string.Join(", ", result.Errors.Select(e => e.Description)));
                await users.AddToRoleAsync(user, Roles.Admin);
                logger.LogInformation("Seeded admin user {Email}", email);
            }
        }

        if (sampleData && !await db.RoomTypes.AnyAsync(ct))
        {
            var standard = RoomTypeWithPlans("STD", "Standard Room", "Cozy room with city view.", 2, 1, 22, 2500m, 0);
            var deluxe = RoomTypeWithPlans("DLX", "Deluxe Sea View", "Spacious room with balcony and sea view.", 2, 2, 32, 4200m, 1);
            var suite = RoomTypeWithPlans("STE", "Family Suite", "Two bedrooms, living area, ideal for families.", 4, 2, 55, 7500m, 2);
            db.RoomTypes.AddRange(standard, deluxe, suite);
            AddRooms(db, standard, 101, 8, 1);
            AddRooms(db, deluxe, 201, 6, 2);
            AddRooms(db, suite, 301, 2, 3);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded sample room types");
        }
    }

    private static RoomType RoomTypeWithPlans(string code, string name, string desc, int adults, int children, int size, decimal price, int order)
    {
        var rt = new RoomType
        {
            Code = code, Name = name, Description = desc, MaxAdults = adults, MaxChildren = children,
            SizeSquareMeters = size, BasePrice = price, Currency = "TRY", SortOrder = order,
            Amenities = ["Wi-Fi", "Air conditioning", "Minibar", "Smart TV"],
        };
        rt.RatePlans.Add(new RatePlan { Code = "BB", Name = "Bed & Breakfast", MealPlan = MealPlan.BedAndBreakfast, FreeCancellationDays = 2 });
        rt.RatePlans.Add(new RatePlan
        {
            Code = "NR", Name = "Non-refundable, Room Only", MealPlan = MealPlan.RoomOnly,
            IsRefundable = false, FreeCancellationDays = null, PriceFactor = 0.85m,
        });
        return rt;
    }

    private static void AddRooms(AppDbContext db, RoomType type, int start, int count, int floor)
    {
        for (var i = 0; i < count; i++)
            db.Rooms.Add(new Room { Number = (start + i).ToString(), Floor = floor, RoomTypeId = type.Id });
    }
}
