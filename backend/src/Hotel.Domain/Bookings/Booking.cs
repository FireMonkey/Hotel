using Hotel.Domain.Common;
using Hotel.Domain.Rooms;

namespace Hotel.Domain.Bookings;

public class Booking : Entity
{
    public required string Reference { get; set; }
    public BookingSource Source { get; set; }
    /// <summary>Reservation id on the OTA / channel manager side.</summary>
    public string? ExternalReference { get; set; }
    public Guid? ChannelConnectionId { get; set; }

    public Guid RoomTypeId { get; set; }
    public RoomType? RoomType { get; set; }
    public Guid? RatePlanId { get; set; }
    public RatePlan? RatePlan { get; set; }
    public Guid? RoomId { get; set; }
    public Room? Room { get; set; }

    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public int Adults { get; set; } = 1;
    public int Children { get; set; }

    public required Guest Guest { get; set; }
    public string? SpecialRequests { get; set; }
    public string? InternalNotes { get; set; }

    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal? CommissionAmount { get; set; }
    public string Currency { get; set; } = "TRY";

    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;
    /// <summary>Set when an OTA booking was imported although no inventory was left.</summary>
    public bool IsOverbooked { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    public uint Version { get; set; }

    public StayPeriod Stay => new(CheckIn, CheckOut);
    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;

    /// <summary>Statuses that consume inventory.</summary>
    public static readonly BookingStatus[] ActiveStatuses = [BookingStatus.Pending, BookingStatus.Confirmed, BookingStatus.CheckedIn];

    public bool IsActive => ActiveStatuses.Contains(Status);

    public void Confirm()
    {
        EnsureStatus(BookingStatus.Pending);
        Status = BookingStatus.Confirmed;
        Touch();
    }

    public void Cancel(string? reason)
    {
        if (Status is BookingStatus.Cancelled) return;
        if (Status is BookingStatus.CheckedIn or BookingStatus.CheckedOut)
            throw new DomainException("invalid_status", $"A {Status} booking cannot be cancelled.");
        Status = BookingStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        CancellationReason = reason;
        Touch();
    }

    public void CheckInGuest(Guid roomId)
    {
        EnsureStatus(BookingStatus.Confirmed, BookingStatus.Pending);
        RoomId = roomId;
        Status = BookingStatus.CheckedIn;
        Touch();
    }

    public void CheckOutGuest()
    {
        EnsureStatus(BookingStatus.CheckedIn);
        Status = BookingStatus.CheckedOut;
        Touch();
    }

    public void MarkNoShow()
    {
        EnsureStatus(BookingStatus.Confirmed, BookingStatus.Pending);
        Status = BookingStatus.NoShow;
        Touch();
    }

    private void EnsureStatus(params BookingStatus[] allowed)
    {
        if (!allowed.Contains(Status))
            throw new DomainException("invalid_status", $"Operation not allowed while booking is {Status}.");
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
