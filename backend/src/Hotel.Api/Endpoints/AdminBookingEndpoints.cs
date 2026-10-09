using Hotel.Application.Bookings;
using Hotel.Application.Dashboard;
using Hotel.Domain.Bookings;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Hotel.Api.Endpoints;

public static class AdminBookingEndpoints
{
    public static void MapAdminBookingEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapGet("/dashboard", (int? days, DashboardService svc, CancellationToken ct) => svc.GetAsync(days ?? 30, ct))
            .WithTags("Admin: Dashboard");

        var g = admin.MapGroup("/bookings").WithTags("Admin: Bookings");

        g.MapGet("/", ([AsParameters] BookingListQuery q, BookingService svc, CancellationToken ct) => svc.ListAsync(q, ct));

        g.MapGet("/{id:guid}", async Task<Results<Ok<BookingDto>, NotFound>> (Guid id, BookingService svc, CancellationToken ct) =>
            await svc.GetAsync(id, ct) is { } b ? TypedResults.Ok(b) : TypedResults.NotFound());

        g.MapPost("/", async (AdminCreateBookingRequest req, BookingService svc, CancellationToken ct) =>
        {
            var b = await svc.CreateAsync(req.Booking, BookingSource.Admin, req.Confirm, ct);
            return TypedResults.Created($"/api/admin/bookings/{b.Id}", b);
        });

        g.MapPatch("/{id:guid}", (Guid id, UpdateBookingRequest req, BookingService svc, CancellationToken ct) => svc.UpdateAsync(id, req, ct));
        g.MapPost("/{id:guid}/confirm", (Guid id, BookingService svc, CancellationToken ct) => svc.ConfirmAsync(id, ct));
        g.MapPost("/{id:guid}/cancel", (Guid id, CancelRequest req, BookingService svc, CancellationToken ct) => svc.CancelAsync(id, req.Reason, ct));
        g.MapPost("/{id:guid}/check-in", (Guid id, CheckInRequest req, BookingService svc, CancellationToken ct) => svc.CheckInAsync(id, req.RoomId, ct));
        g.MapPost("/{id:guid}/check-out", (Guid id, BookingService svc, CancellationToken ct) => svc.CheckOutAsync(id, ct));
        g.MapPost("/{id:guid}/no-show", (Guid id, BookingService svc, CancellationToken ct) => svc.NoShowAsync(id, ct));
    }

    public record AdminCreateBookingRequest(CreateBookingRequest Booking, bool Confirm = true);
    public record CancelRequest(string? Reason);
    public record CheckInRequest(Guid RoomId);
}
