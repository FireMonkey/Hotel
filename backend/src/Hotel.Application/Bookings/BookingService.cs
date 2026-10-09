using Hotel.Application.Abstractions;
using Hotel.Application.Ari;
using Hotel.Application.Availability;
using Hotel.Application.Common;
using Hotel.Domain.Bookings;
using Hotel.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Hotel.Application.Bookings;

public class BookingService(
    IAppDbContext db,
    AvailabilityService availability,
    IInventoryLock inventoryLock,
    INotificationService notifications,
    TimeProvider clock)
{
    public async Task<BookingDto> CreateAsync(CreateBookingRequest req, BookingSource source, bool autoConfirm, CancellationToken ct)
    {
        ValidateGuest(req.Guest);
        var stay = new StayPeriod(req.CheckIn, req.CheckOut);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (source == BookingSource.Direct && stay.CheckIn < today.AddDays(-1))
            throw new DomainException("invalid_stay", "Check-in cannot be in the past.");
        if (stay.Nights > AvailabilityService.MaxNights && source == BookingSource.Direct)
            throw new DomainException("invalid_stay", $"Maximum stay is {AvailabilityService.MaxNights} nights.");

        var ratePlan = await db.RatePlans.AsNoTracking().Include(rp => rp.RoomType)
            .FirstOrDefaultAsync(rp => rp.Id == req.RatePlanId, ct)
            ?? throw new DomainException("rate_plan_not_found", "Rate plan not found.");
        var roomType = ratePlan.RoomType!;
        if (!ratePlan.IsActive || !roomType.IsActive)
            throw new DomainException("rate_not_available", "This room is not bookable.");
        if (req.Adults < 1 || req.Adults > roomType.MaxAdults || req.Children < 0
            || req.Adults + req.Children > roomType.MaxAdults + roomType.MaxChildren)
            throw new DomainException("invalid_occupancy", "Guest count exceeds room capacity.");

        await using var scope = await inventoryLock.BeginAsync([roomType.Id], ct);

        var inventory = await availability.GetInventoryAsync([roomType.Id], stay.CheckIn, stay.CheckOut, ct);
        if (inventory[roomType.Id].Any(d => d.Available <= 0))
            throw new DomainException("no_availability", "No rooms left for the selected dates.");

        var quote = await availability.QuoteAsync(ratePlan.Id, stay.CheckIn, stay.CheckOut, ct);
        if (quote is null && req.TotalAmountOverride is null)
            throw new DomainException("rate_not_available", "This rate is not available for the selected dates.");

        var booking = new Booking
        {
            Reference = ReferenceGenerator.NewBookingReference(),
            Source = source,
            RoomTypeId = roomType.Id,
            RatePlanId = ratePlan.Id,
            CheckIn = stay.CheckIn,
            CheckOut = stay.CheckOut,
            Adults = req.Adults,
            Children = req.Children,
            Guest = ToGuest(req.Guest),
            SpecialRequests = req.SpecialRequests?.Trim(),
            InternalNotes = req.InternalNotes,
            TotalAmount = req.TotalAmountOverride ?? quote!.TotalPrice,
            Currency = roomType.Currency,
            Status = autoConfirm ? BookingStatus.Confirmed : BookingStatus.Pending,
        };
        db.Bookings.Add(booking);
        AriOutbox.Enqueue(db, roomType.Id, stay.CheckIn, stay.CheckOut);
        await db.SaveChangesAsync(ct);
        await scope.CommitAsync(ct);

        await notifications.BookingCreatedAsync(booking, ct);
        return (await GetAsync(booking.Id, ct))!;
    }

    public async Task<BookingDto?> GetAsync(Guid id, CancellationToken ct) =>
        await Project(db.Bookings.AsNoTracking().Where(b => b.Id == id)).FirstOrDefaultAsync(ct);

    public async Task<PagedResult<BookingDto>> ListAsync(BookingListQuery q, CancellationToken ct)
    {
        var query = db.Bookings.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.Trim().ToLower();
            query = query.Where(b => b.Reference.ToLower().Contains(s)
                                     || (b.ExternalReference != null && b.ExternalReference.ToLower().Contains(s))
                                     || b.Guest.LastName.ToLower().Contains(s)
                                     || b.Guest.FirstName.ToLower().Contains(s)
                                     || b.Guest.Email.ToLower().Contains(s));
        }
        if (q.Status is { } status) query = query.Where(b => b.Status == status);
        if (q.Source is { } source) query = query.Where(b => b.Source == source);
        if (q.ArrivalFrom is { } af) query = query.Where(b => b.CheckIn >= af);
        if (q.ArrivalTo is { } at) query = query.Where(b => b.CheckIn <= at);
        if (q.StayDate is { } sd) query = query.Where(b => b.CheckIn <= sd && sd < b.CheckOut);
        if (q.Overbooked == true) query = query.Where(b => b.IsOverbooked && b.Status != BookingStatus.Cancelled);

        var pageSize = Math.Clamp(q.PageSize, 1, 200);
        var page = Math.Max(1, q.Page);
        var total = await query.CountAsync(ct);
        var items = await Project(query.OrderByDescending(b => b.CreatedAt))
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<BookingDto>(items, page, pageSize, total);
    }

    public async Task<BookingDto> UpdateAsync(Guid id, UpdateBookingRequest req, CancellationToken ct)
    {
        var b = await Load(id, ct);
        if (req.InternalNotes is not null) b.InternalNotes = req.InternalNotes;
        if (req.SpecialRequests is not null) b.SpecialRequests = req.SpecialRequests;
        if (req.PaidAmount is { } paid)
        {
            if (paid < 0) throw new DomainException("invalid_amount", "Paid amount cannot be negative.");
            b.PaidAmount = paid;
            b.PaymentStatus = paid == 0 ? PaymentStatus.Unpaid : paid >= b.TotalAmount ? PaymentStatus.Paid : PaymentStatus.PartiallyPaid;
        }
        if (req.PaymentStatus is { } ps) b.PaymentStatus = ps;
        if (req.Guest is not null)
        {
            ValidateGuest(req.Guest);
            b.Guest = ToGuest(req.Guest);
        }
        b.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public Task<BookingDto> ConfirmAsync(Guid id, CancellationToken ct) => Transition(id, b => b.Confirm(), false, ct);

    public async Task<BookingDto> CancelAsync(Guid id, string? reason, CancellationToken ct)
    {
        var dto = await Transition(id, b => b.Cancel(reason), true, ct);
        var booking = await db.Bookings.AsNoTracking().FirstAsync(b => b.Id == id, ct);
        await notifications.BookingCancelledAsync(booking, ct);
        return dto;
    }

    public async Task<BookingDto> CheckInAsync(Guid id, Guid roomId, CancellationToken ct)
    {
        var b = await Load(id, ct);
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == roomId, ct)
                   ?? throw new DomainException("room_not_found", "Room not found.");
        if (room.RoomTypeId != b.RoomTypeId)
            throw new DomainException("room_type_mismatch", "Room belongs to a different room type.");
        var occupied = await db.Bookings.AnyAsync(x => x.RoomId == roomId && x.Id != id
            && x.Status == BookingStatus.CheckedIn, ct);
        if (occupied) throw new DomainException("room_occupied", $"Room {room.Number} is occupied.");
        b.CheckInGuest(roomId);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<BookingDto> CheckOutAsync(Guid id, CancellationToken ct)
    {
        var dto = await Transition(id, b => b.CheckOutGuest(), false, ct);
        var b = await db.Bookings.Include(x => x.Room).FirstAsync(x => x.Id == id, ct);
        if (b.Room is not null) b.Room.Housekeeping = Domain.Rooms.HousekeepingStatus.Dirty;
        await db.SaveChangesAsync(ct);
        return dto;
    }

    public Task<BookingDto> NoShowAsync(Guid id, CancellationToken ct) => Transition(id, b => b.MarkNoShow(), true, ct);

    public async Task<PublicBookingDto?> FindForGuestAsync(string reference, string email, CancellationToken ct)
    {
        var b = await db.Bookings.AsNoTracking().Include(x => x.RoomType).Include(x => x.RatePlan)
            .FirstOrDefaultAsync(x => x.Reference == reference.Trim().ToUpper(), ct);
        if (b is null || !string.Equals(b.Guest.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;
        return new PublicBookingDto(b.Reference, b.RoomType!.Name, b.RatePlan?.Name, b.CheckIn, b.CheckOut, b.Nights,
            b.Adults, b.Children, b.Guest.FullName, b.TotalAmount, b.Currency, b.Status, CanGuestCancel(b));
    }

    public async Task<PublicBookingDto?> CancelByGuestAsync(string reference, string email, CancellationToken ct)
    {
        var found = await FindForGuestAsync(reference, email, ct);
        if (found is null) return null;
        if (!found.CanCancel)
            throw new DomainException("cancellation_not_allowed", "This booking can no longer be cancelled online.");
        var id = await db.Bookings.Where(b => b.Reference == found.Reference).Select(b => b.Id).FirstAsync(ct);
        await CancelAsync(id, "Cancelled by guest", ct);
        return await FindForGuestAsync(reference, email, ct);
    }

    private bool CanGuestCancel(Booking b)
    {
        if (b.Source is not (BookingSource.Direct or BookingSource.Admin) || !b.IsActive || b.Status == BookingStatus.CheckedIn)
            return false;
        var days = b.RatePlan?.FreeCancellationDays;
        if (b.RatePlan is { IsRefundable: false } || days is null) return false;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return today.AddDays(days.Value) <= b.CheckIn;
    }

    private async Task<BookingDto> Transition(Guid id, Action<Booking> action, bool releasesInventory, CancellationToken ct)
    {
        var b = await Load(id, ct);
        action(b);
        if (releasesInventory) AriOutbox.Enqueue(db, b.RoomTypeId, b.CheckIn, b.CheckOut);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    private async Task<Booking> Load(Guid id, CancellationToken ct) =>
        await db.Bookings.FirstOrDefaultAsync(b => b.Id == id, ct)
        ?? throw new KeyNotFoundException($"Booking {id} not found.");

    internal static Guest ToGuest(GuestDto g) => new()
    {
        FirstName = g.FirstName.Trim(),
        LastName = g.LastName.Trim(),
        Email = g.Email.Trim().ToLowerInvariant(),
        Phone = g.Phone?.Trim(),
        Country = g.Country?.Trim(),
    };

    private static void ValidateGuest(GuestDto g)
    {
        if (string.IsNullOrWhiteSpace(g.FirstName) || string.IsNullOrWhiteSpace(g.LastName))
            throw new DomainException("invalid_guest", "Guest name is required.");
        if (string.IsNullOrWhiteSpace(g.Email) || !g.Email.Contains('@') || g.Email.Length > 254)
            throw new DomainException("invalid_guest", "A valid e-mail address is required.");
    }

    internal IQueryable<BookingDto> Project(IQueryable<Booking> q) =>
        q.Select(b => new BookingDto(
            b.Id, b.Reference, b.Source, b.ExternalReference,
            db.ChannelConnections.Where(c => c.Id == b.ChannelConnectionId).Select(c => c.Name).FirstOrDefault(),
            b.RoomTypeId, b.RoomType!.Name, b.RatePlanId, b.RatePlan != null ? b.RatePlan.Name : null,
            b.RoomId, b.Room != null ? b.Room.Number : null,
            b.CheckIn, b.CheckOut, b.CheckOut.DayNumber - b.CheckIn.DayNumber, b.Adults, b.Children,
            new GuestDto(b.Guest.FirstName, b.Guest.LastName, b.Guest.Email, b.Guest.Phone, b.Guest.Country),
            b.SpecialRequests, b.InternalNotes, b.TotalAmount, b.PaidAmount, b.CommissionAmount, b.Currency,
            b.Status, b.PaymentStatus, b.IsOverbooked, b.CreatedAt, b.CancelledAt, b.CancellationReason));
}
