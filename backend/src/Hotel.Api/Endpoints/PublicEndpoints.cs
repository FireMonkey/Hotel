using Hotel.Api.Infrastructure;
using Hotel.Application.Availability;
using Hotel.Application.Bookings;
using Hotel.Domain.Bookings;
using Hotel.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hotel.Api.Endpoints;

public static class PublicEndpoints
{
    public const string BookingRateLimit = "public-booking";

    public static void MapPublicEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/public").WithTags("Public");

        g.MapGet("/hotel", (IOptions<HotelOptions> o) =>
        {
            var h = o.Value;
            return new
            {
                h.Name, h.Tagline, h.Description, h.Address, h.City, h.Country, h.Phone, h.Email,
                h.CheckInTime, h.CheckOutTime, h.Currency, h.Latitude, h.Longitude, h.ImageUrls, h.Facilities,
            };
        });

        g.MapGet("/room-types", async (AppDbContext db, CancellationToken ct) =>
        {
            var types = await db.RoomTypes.AsNoTracking().Include(r => r.RatePlans)
                .Where(r => r.IsActive).OrderBy(r => r.SortOrder).ThenBy(r => r.Name).ToListAsync(ct);
            return types.Select(r => new
            {
                r.Id, r.Code, r.Name, r.Description, r.MaxAdults, r.MaxChildren, r.SizeSquareMeters,
                FromPrice = Math.Round(r.BasePrice * r.RatePlans.Where(p => p.IsActive).Select(p => p.PriceFactor).DefaultIfEmpty(1m).Min(), 2),
                r.Currency, r.Amenities, r.ImageUrls,
            });
        });

        g.MapGet("/availability", (DateOnly checkIn, DateOnly checkOut, int? adults, int? children, Guid? roomTypeId,
                AvailabilityService svc, CancellationToken ct) =>
            svc.SearchAsync(new AvailabilityQuery(checkIn, checkOut, adults ?? 2, children ?? 0, roomTypeId), ct));

        g.MapPost("/bookings", async Task<Created<PublicBookingDto>> (PublicCreateBookingRequest req, BookingService svc,
            IOptions<HotelOptions> hotel, CancellationToken ct) =>
        {
            var created = await svc.CreateAsync(
                new CreateBookingRequest(req.RatePlanId, req.CheckIn, req.CheckOut, req.Adults, req.Children, req.Guest, req.SpecialRequests),
                BookingSource.Direct, hotel.Value.AutoConfirmDirectBookings, ct);
            var dto = (await svc.FindForGuestAsync(created.Reference, created.Guest.Email, ct))!;
            return TypedResults.Created($"/api/public/bookings/{dto.Reference}", dto);
        }).RequireRateLimiting(BookingRateLimit);

        // Guests look up / cancel their booking with reference + e-mail (no account needed).
        g.MapPost("/bookings/lookup", async Task<Results<Ok<PublicBookingDto>, NotFound>> (GuestLookupRequest req, BookingService svc, CancellationToken ct) =>
            await svc.FindForGuestAsync(req.Reference, req.Email, ct) is { } b ? TypedResults.Ok(b) : TypedResults.NotFound())
            .RequireRateLimiting(BookingRateLimit);

        g.MapPost("/bookings/cancel", async Task<Results<Ok<PublicBookingDto>, NotFound>> (GuestLookupRequest req, BookingService svc, CancellationToken ct) =>
            await svc.CancelByGuestAsync(req.Reference, req.Email, ct) is { } b ? TypedResults.Ok(b) : TypedResults.NotFound())
            .RequireRateLimiting(BookingRateLimit);
    }

    public record PublicCreateBookingRequest(Guid RatePlanId, DateOnly CheckIn, DateOnly CheckOut, int Adults, int Children, GuestDto Guest, string? SpecialRequests);

    public record GuestLookupRequest(string Reference, string Email);
}
