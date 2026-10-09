using Hotel.Application.Ari;
using Hotel.Application.Bookings;
using Hotel.Domain.Bookings;
using Hotel.Domain.Channels;

namespace Hotel.Application.Channels;

/// <summary>
/// One implementation per OTA / channel manager. Connectors only translate between the
/// channel's wire format and the channel-agnostic models below; all business rules
/// (overbooking, mapping, outbox, logging) live in <see cref="ChannelSyncService"/>.
/// </summary>
public interface IChannelConnector
{
    ChannelType Type { get; }

    /// <summary>Describes the settings the admin UI should ask for.</summary>
    ChannelDescriptor Describe();

    Task<ConnectionTestResult> TestConnectionAsync(ChannelContext ctx, CancellationToken ct);

    /// <summary>Push availability, rates and restrictions. Must be idempotent.</summary>
    Task PushAriAsync(ChannelContext ctx, AriSnapshot snapshot, CancellationToken ct);

    Task<ReservationFetchResult> FetchReservationsAsync(ChannelContext ctx, CancellationToken ct);

    /// <summary>Tell the channel these reservations were stored (so they are not delivered again).</summary>
    Task AcknowledgeAsync(ChannelContext ctx, IReadOnlyList<ExternalReservation> processed, CancellationToken ct);
}

public record ChannelDescriptor(
    ChannelType Type,
    string DisplayName,
    string Description,
    bool SupportsAriPush,
    bool SupportsReservationPull,
    bool RequiresPartnerCertification,
    IReadOnlyList<ChannelSettingField> Fields,
    string? DocumentationUrl);

public record ChannelSettingField(string Key, string Label, bool IsSecret = false, bool Required = true, string? Placeholder = null, string? Help = null);

public record ConnectionTestResult(bool Success, string Message);

public enum ExternalReservationStatus { New = 0, Modified = 1, Cancelled = 2 }

public record ExternalReservation(
    string ExternalId,
    ExternalReservationStatus Status,
    string ExternalRoomId,
    string? ExternalRateId,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Adults,
    int Children,
    GuestDto Guest,
    decimal TotalAmount,
    string Currency,
    decimal? Commission = null,
    string? Notes = null,
    /// <summary>Provider token needed for acknowledgement (e.g. revision id).</summary>
    string? AckToken = null,
    /// <summary>For channel managers: the OTA the booking actually came from.</summary>
    BookingSource? Source = null,
    bool PaidToChannel = false);

/// <param name="IsFullSnapshot">
/// True when the feed lists every current reservation (iCal). Active bookings of this
/// connection missing from the feed are then treated as cancelled.
/// </param>
public record ReservationFetchResult(IReadOnlyList<ExternalReservation> Reservations, bool IsFullSnapshot = false)
{
    public static readonly ReservationFetchResult Empty = new([]);
}

public class ChannelContext(ChannelConnection connection, string? secret, IReadOnlyList<ChannelMapping> mappings)
{
    public ChannelConnection Connection { get; } = connection;
    public string? Secret { get; } = secret;
    public IReadOnlyList<ChannelMapping> Mappings { get; } = mappings;

    public string Setting(string key) =>
        Connection.Settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : throw new ChannelException($"Setting '{key}' is not configured.");

    public string? SettingOrDefault(string key, string? fallback = null) =>
        Connection.Settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    public string RequireSecret() => Secret ?? throw new ChannelException("Credentials are not configured.");

    public string RequirePropertyId() =>
        Connection.ExternalPropertyId ?? throw new ChannelException("External property / hotel id is not configured.");

    public ChannelMapping? RoomMapping(Guid roomTypeId) =>
        Mappings.FirstOrDefault(m => m.RoomTypeId == roomTypeId && m.RatePlanId == null)
        ?? Mappings.FirstOrDefault(m => m.RoomTypeId == roomTypeId);

    public ChannelMapping? RateMapping(Guid ratePlanId) => Mappings.FirstOrDefault(m => m.RatePlanId == ratePlanId);
}

public class ChannelException(string message, Exception? inner = null) : Exception(message, inner);
