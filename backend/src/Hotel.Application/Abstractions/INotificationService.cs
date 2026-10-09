using Hotel.Domain.Bookings;

namespace Hotel.Application.Abstractions;

/// <summary>Guest / staff notifications (e-mail, SMS…). Default implementation only logs.</summary>
public interface INotificationService
{
    Task BookingCreatedAsync(Booking booking, CancellationToken ct);
    Task BookingCancelledAsync(Booking booking, CancellationToken ct);
}
