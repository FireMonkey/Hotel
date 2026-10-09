using Hotel.Application.Ari;
using Hotel.Domain.Bookings;
using Hotel.Domain.Common;
using Hotel.Domain.Rooms;
using Hotel.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Api.Endpoints;

public static class AdminRoomEndpoints
{
    public static void MapAdminRoomEndpoints(this RouteGroupBuilder admin)
    {
        // ---- Room types -------------------------------------------------------------------
        var types = admin.MapGroup("/room-types").WithTags("Admin: Room types");

        types.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            await db.RoomTypes.AsNoTracking().Include(r => r.RatePlans)
                .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
                .Select(r => new
                {
                    r.Id, r.Code, r.Name, r.Description, r.MaxAdults, r.MaxChildren, r.SizeSquareMeters, r.BasePrice,
                    r.Currency, r.Amenities, r.ImageUrls, r.IsActive, r.SortOrder,
                    RoomCount = r.Rooms.Count,
                    RatePlans = r.RatePlans.OrderBy(p => p.PriceFactor).Select(p => new
                    {
                        p.Id, p.Code, p.Name, p.MealPlan, p.IsRefundable, p.FreeCancellationDays, p.PriceFactor, p.IsActive,
                    }),
                }).ToListAsync(ct));

        types.MapPost("/", async (RoomTypeInput input, AppDbContext db, CancellationToken ct) =>
        {
            var rt = new RoomType { Code = "", Name = "" };
            input.Apply(rt);
            db.RoomTypes.Add(rt);
            await Save(db, ct);
            return TypedResults.Created($"/api/admin/room-types/{rt.Id}", new { rt.Id });
        });

        types.MapPut("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, RoomTypeInput input, AppDbContext db, CancellationToken ct) =>
        {
            var rt = await db.RoomTypes.FindAsync([id], ct);
            if (rt is null) return TypedResults.NotFound();
            var priceChanged = rt.BasePrice != input.BasePrice || rt.IsActive != input.IsActive;
            input.Apply(rt);
            rt.UpdatedAt = DateTimeOffset.UtcNow;
            if (priceChanged) EnqueueFullRange(db, id);
            await Save(db, ct);
            return TypedResults.NoContent();
        });

        types.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var rt = await db.RoomTypes.FindAsync([id], ct);
            if (rt is null) return TypedResults.NotFound();
            if (await db.Bookings.AnyAsync(b => b.RoomTypeId == id, ct) || await db.Rooms.AnyAsync(r => r.RoomTypeId == id, ct))
                throw new DomainException("in_use", "Room type has rooms or bookings. Deactivate it instead.");
            db.RoomTypes.Remove(rt);
            await Save(db, ct);
            return TypedResults.NoContent();
        });

        // ---- Rate plans -------------------------------------------------------------------
        types.MapPost("/{roomTypeId:guid}/rate-plans", async (Guid roomTypeId, RatePlanInput input, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.RoomTypes.AnyAsync(r => r.Id == roomTypeId, ct)) throw new KeyNotFoundException("Room type not found.");
            var rp = new RatePlan { Code = "", Name = "", RoomTypeId = roomTypeId };
            input.Apply(rp);
            db.RatePlans.Add(rp);
            await Save(db, ct);
            return TypedResults.Created($"/api/admin/rate-plans/{rp.Id}", new { rp.Id });
        });

        admin.MapPut("/rate-plans/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, RatePlanInput input, AppDbContext db, CancellationToken ct) =>
        {
            var rp = await db.RatePlans.FindAsync([id], ct);
            if (rp is null) return TypedResults.NotFound();
            input.Apply(rp);
            rp.UpdatedAt = DateTimeOffset.UtcNow;
            EnqueueFullRange(db, rp.RoomTypeId);
            await Save(db, ct);
            return TypedResults.NoContent();
        }).WithTags("Admin: Room types");

        admin.MapDelete("/rate-plans/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var rp = await db.RatePlans.FindAsync([id], ct);
            if (rp is null) return TypedResults.NotFound();
            if (await db.Bookings.AnyAsync(b => b.RatePlanId == id, ct))
                throw new DomainException("in_use", "Rate plan has bookings. Deactivate it instead.");
            db.RatePlans.Remove(rp);
            await Save(db, ct);
            return TypedResults.NoContent();
        }).WithTags("Admin: Room types");

        // ---- Rooms ------------------------------------------------------------------------
        var rooms = admin.MapGroup("/rooms").WithTags("Admin: Rooms");

        rooms.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return await db.Rooms.AsNoTracking().OrderBy(r => r.Number).Select(r => new
            {
                r.Id, r.Number, r.Floor, r.RoomTypeId, RoomTypeName = r.RoomType!.Name, r.Status, r.Housekeeping, r.Notes,
                CurrentBooking = db.Bookings.Where(b => b.RoomId == r.Id && b.Status == BookingStatus.CheckedIn)
                    .Select(b => new { b.Id, b.Reference, GuestName = b.Guest.FirstName + " " + b.Guest.LastName, b.CheckOut })
                    .FirstOrDefault(),
            }).ToListAsync(ct);
        });

        rooms.MapPost("/", async (RoomInput input, AppDbContext db, CancellationToken ct) =>
        {
            var room = new Room { Number = "" };
            input.Apply(room);
            db.Rooms.Add(room);
            EnqueueFullRange(db, room.RoomTypeId);
            await Save(db, ct);
            return TypedResults.Created($"/api/admin/rooms/{room.Id}", new { room.Id });
        });

        rooms.MapPut("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, RoomInput input, AppDbContext db, CancellationToken ct) =>
        {
            var room = await db.Rooms.FindAsync([id], ct);
            if (room is null) return TypedResults.NotFound();
            var oldType = room.RoomTypeId;
            input.Apply(room);
            room.UpdatedAt = DateTimeOffset.UtcNow;
            EnqueueFullRange(db, oldType);
            if (oldType != room.RoomTypeId) EnqueueFullRange(db, room.RoomTypeId);
            await Save(db, ct);
            return TypedResults.NoContent();
        });

        rooms.MapPatch("/{id:guid}/housekeeping", async Task<Results<NoContent, NotFound>> (Guid id, HousekeepingInput input, AppDbContext db, CancellationToken ct) =>
        {
            var room = await db.Rooms.FindAsync([id], ct);
            if (room is null) return TypedResults.NotFound();
            room.Housekeeping = input.Housekeeping;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        rooms.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var room = await db.Rooms.FindAsync([id], ct);
            if (room is null) return TypedResults.NotFound();
            if (await db.Bookings.AnyAsync(b => b.RoomId == id, ct))
                throw new DomainException("in_use", "Room has booking history. Set it out of order instead.");
            db.Rooms.Remove(room);
            EnqueueFullRange(db, room.RoomTypeId);
            await Save(db, ct);
            return TypedResults.NoContent();
        });
    }

    private static void EnqueueFullRange(AppDbContext db, Guid roomTypeId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        AriOutbox.Enqueue(db, roomTypeId, today, today.AddDays(Hotel.Application.Channels.ChannelSyncService.PushHorizonDays));
    }

    private static async Task Save(AppDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true
                                           || ex.InnerException?.Message.Contains("23505") == true)
        {
            throw new DomainException("duplicate", "A record with the same code / number already exists.");
        }
    }

    public record RoomTypeInput(string Code, string Name, string? Description, int MaxAdults, int MaxChildren, int SizeSquareMeters,
        decimal BasePrice, string Currency, List<string>? Amenities, List<string>? ImageUrls, bool IsActive = true, int SortOrder = 0)
    {
        public void Apply(RoomType rt)
        {
            if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name)) throw new DomainException("invalid_input", "Code and name are required.");
            if (MaxAdults < 1 || MaxChildren < 0) throw new DomainException("invalid_input", "Invalid occupancy.");
            if (BasePrice <= 0) throw new DomainException("invalid_input", "Base price must be positive.");
            if (Currency?.Length != 3) throw new DomainException("invalid_input", "Currency must be an ISO-4217 code.");
            rt.Code = Code.Trim().ToUpperInvariant(); rt.Name = Name.Trim(); rt.Description = Description;
            rt.MaxAdults = MaxAdults; rt.MaxChildren = MaxChildren; rt.SizeSquareMeters = SizeSquareMeters;
            rt.BasePrice = BasePrice; rt.Currency = Currency.ToUpperInvariant();
            rt.Amenities = Amenities ?? []; rt.ImageUrls = ImageUrls ?? []; rt.IsActive = IsActive; rt.SortOrder = SortOrder;
        }
    }

    public record RatePlanInput(string Code, string Name, MealPlan MealPlan, bool IsRefundable, int? FreeCancellationDays,
        decimal PriceFactor = 1m, bool IsActive = true)
    {
        public void Apply(RatePlan rp)
        {
            if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name)) throw new DomainException("invalid_input", "Code and name are required.");
            if (PriceFactor is <= 0 or > 10) throw new DomainException("invalid_input", "Price factor must be between 0 and 10.");
            rp.Code = Code.Trim().ToUpperInvariant(); rp.Name = Name.Trim(); rp.MealPlan = MealPlan;
            rp.IsRefundable = IsRefundable; rp.FreeCancellationDays = IsRefundable ? FreeCancellationDays : null;
            rp.PriceFactor = PriceFactor; rp.IsActive = IsActive;
        }
    }

    public record RoomInput(string Number, int Floor, Guid RoomTypeId, RoomStatus Status, string? Notes)
    {
        public void Apply(Room r)
        {
            if (string.IsNullOrWhiteSpace(Number)) throw new DomainException("invalid_input", "Room number is required.");
            r.Number = Number.Trim(); r.Floor = Floor; r.RoomTypeId = RoomTypeId; r.Status = Status; r.Notes = Notes;
        }
    }

    public record HousekeepingInput(HousekeepingStatus Housekeeping);
}
