using Hotel.Domain.Common;
using Hotel.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Api.Endpoints;

public static class AdminUserEndpoints
{
    public static void MapAdminUserEndpoints(this RouteGroupBuilder admin)
    {
        var g = admin.MapGroup("/users").WithTags("Admin: Users").RequireAuthorization(p => p.RequireRole(Roles.Admin));

        g.MapGet("/", async (UserManager<AppUser> users) =>
        {
            var list = await users.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();
            var result = new List<object>();
            foreach (var u in list)
                result.Add(new { u.Id, u.Email, u.FullName, Roles = await users.GetRolesAsync(u), Locked = u.LockoutEnd > DateTimeOffset.UtcNow });
            return result;
        });

        g.MapPost("/", async (CreateUserRequest req, UserManager<AppUser> users) =>
        {
            if (req.Role is not (Roles.Admin or Roles.Staff)) throw new DomainException("invalid_input", "Unknown role.");
            var user = new AppUser { UserName = req.Email, Email = req.Email, EmailConfirmed = true, FullName = req.FullName };
            var result = await users.CreateAsync(user, req.Password);
            if (!result.Succeeded) throw new DomainException("invalid_input", string.Join(" ", result.Errors.Select(e => e.Description)));
            await users.AddToRoleAsync(user, req.Role);
            return Results.Created($"/api/admin/users/{user.Id}", new { user.Id });
        });

        g.MapDelete("/{id:guid}", async (Guid id, UserManager<AppUser> users, HttpContext http) =>
        {
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null) return Results.NotFound();
            if (users.GetUserId(http.User) == id.ToString()) throw new DomainException("invalid_input", "You cannot delete yourself.");
            await users.DeleteAsync(user);
            return Results.NoContent();
        });
    }

    public record CreateUserRequest(string Email, string FullName, string Password, string Role);
}
