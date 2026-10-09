using Hotel.Application.Abstractions;
using Hotel.Domain.Bookings;
using Microsoft.Extensions.Logging;

namespace Hotel.Infrastructure.Services;

/// <summary>Placeholder until an e-mail/SMS provider is chosen (SMTP, SendGrid, Netgsm…).</summary>
public class LoggingNotificationService(ILogger<LoggingNotificationService> logger) : INotificationService
{
    public Task BookingCreatedAsync(Booking booking, CancellationToken ct)
    {
        logger.LogInformation("Booking {Reference} created ({Source}) for {Email}, {CheckIn}→{CheckOut}",
            booking.Reference, booking.Source, booking.Guest.Email, booking.CheckIn, booking.CheckOut);
        return Task.CompletedTask;
    }

    public Task BookingCancelledAsync(Booking booking, CancellationToken ct)
    {
        logger.LogInformation("Booking {Reference} cancelled: {Reason}", booking.Reference, booking.CancellationReason);
        return Task.CompletedTask;
    }
}
