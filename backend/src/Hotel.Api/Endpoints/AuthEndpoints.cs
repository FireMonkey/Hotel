using System.Security.Claims;
using Hotel.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Hotel.Api.Endpoints;

/// <summary>
/// Login/refresh for the admin panel using ASP.NET Core Identity bearer tokens.
/// (MapIdentityApi is not used because it exposes public self-registration.)
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth");

        g.MapPost("/login", async Task<Results<EmptyHttpResult, ProblemHttpResult>> (LoginRequest req, SignInManager<AppUser> signIn) =>
        {
            signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
            var result = await signIn.PasswordSignInAsync(req.Email, req.Password, isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded) return TypedResults.Empty; // token response is written by the bearer handler
            return TypedResults.Problem(result.IsLockedOut ? "Account locked. Try again later." : "Invalid e-mail or password.",
                statusCode: StatusCodes.Status401Unauthorized);
        }).RequireRateLimiting(PublicEndpoints.BookingRateLimit);

        g.MapPost("/refresh", async Task<Results<SignInHttpResult, ChallengeHttpResult>> (RefreshRequest req,
            IOptionsMonitor<BearerTokenOptions> options, SignInManager<AppUser> signIn, TimeProvider clock) =>
        {
            var protector = options.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
            var ticket = protector.Unprotect(req.RefreshToken);
            if (ticket?.Properties.ExpiresUtc is not { } expires || clock.GetUtcNow() >= expires
                || await signIn.ValidateSecurityStampAsync(ticket.Principal) is not { } user)
                return TypedResults.Challenge();
            var principal = await signIn.CreateUserPrincipalAsync(user);
            return TypedResults.SignIn(principal, authenticationScheme: IdentityConstants.BearerScheme);
        });

        g.MapGet("/me", async (ClaimsPrincipal principal, UserManager<AppUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            return Results.Ok(new { user.Id, user.Email, user.FullName, Roles = await users.GetRolesAsync(user) });
        }).RequireAuthorization();

        g.MapPost("/change-password", async (ChangePasswordRequest req, ClaimsPrincipal principal, UserManager<AppUser> users) =>
        {
            var user = await users.GetUserAsync(principal);
            if (user is null) return Results.Unauthorized();
            var result = await users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
            return result.Succeeded ? Results.NoContent()
                : Results.ValidationProblem(result.Errors.GroupBy(e => e.Code).ToDictionary(e => e.Key, e => e.Select(x => x.Description).ToArray()));
        }).RequireAuthorization();
    }

    public record LoginRequest(string Email, string Password);
    public record RefreshRequest(string RefreshToken);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
}
