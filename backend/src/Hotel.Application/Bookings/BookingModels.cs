using Hotel.Domain.Bookings;

namespace Hotel.Application.Bookings;

public record GuestDto(string FirstName, string LastName, string Email, string? Phone, string? Country);

public record CreateBookingRequest(
    Guid RatePlanId,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Adults,
    int Children,
    GuestDto Guest,
    string? SpecialRequests,
    /// <summary>Admin only: overrides the computed total.</summary>
    decimal? TotalAmountOverride = null,
    string? InternalNotes = null);

public record BookingDto(
    Guid Id,
    string Reference,
    BookingSource Source,
    string? ExternalReference,
    string? ChannelName,
    Guid RoomTypeId,
    string RoomTypeName,
    Guid? RatePlanId,
    string? RatePlanName,
    Guid? RoomId,
    string? RoomNumber,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Nights,
    int Adults,
    int Children,
    GuestDto Guest,
    string? SpecialRequests,
    string? InternalNotes,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal? CommissionAmount,
    string Currency,
    BookingStatus Status,
    PaymentStatus PaymentStatus,
    bool IsOverbooked,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt,
    string? CancellationReason);

public record BookingListQuery(
    string? Search = null,
    BookingStatus? Status = null,
    BookingSource? Source = null,
    DateOnly? ArrivalFrom = null,
    DateOnly? ArrivalTo = null,
    DateOnly? StayDate = null,
    bool? Overbooked = null,
    int Page = 1,
    int PageSize = 25);

public record UpdateBookingRequest(string? InternalNotes, decimal? PaidAmount, PaymentStatus? PaymentStatus, GuestDto? Guest, string? SpecialRequests);

/// <summary>What a guest sees when looking up their own booking — no internal data.</summary>
public record PublicBookingDto(
    string Reference,
    string RoomTypeName,
    string? RatePlanName,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Nights,
    int Adults,
    int Children,
    string GuestName,
    decimal TotalAmount,
    string Currency,
    BookingStatus Status,
    bool CanCancel);
