using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hotel.Application.Ari;
using Hotel.Application.Bookings;
using Hotel.Application.Channels;
using Hotel.Domain.Bookings;
using Hotel.Domain.Channels;
using static Hotel.Infrastructure.Channels.XmlChannelClient;

namespace Hotel.Infrastructure.Channels.Channex;

/// <summary>
/// Channex.io channel manager (REST/JSON). One integration distributes ARI to and collects
/// bookings from Booking.com, Expedia, Airbnb, Agoda, Trip.com, Hotelbeds … — the recommended
/// route for a single property because no per-OTA certification is needed.
/// Docs: https://docs.channex.io
/// </summary>
public class ChannexConnector(IHttpClientFactory httpFactory) : IChannelConnector
{
    public const string HttpClientName = "channex";
    private const string DefaultBaseUrl = "https://app.channex.io/api/v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public ChannelType Type => ChannelType.Channex;

    public ChannelDescriptor Describe() => new(
        Type, "Channex (Channel Manager)",
        "Booking.com, Expedia, Airbnb, Agoda and 50+ OTAs through a single API. Recommended for most hotels.",
        SupportsAriPush: true, SupportsReservationPull: true, RequiresPartnerCertification: false,
        [
            new("secret", "API key", IsSecret: true),
            new("baseUrl", "API base URL", Required: false, Placeholder: DefaultBaseUrl,
                Help: "Use https://staging.channex.io/api/v1 for the sandbox."),
        ],
        "https://docs.channex.io");

    public async Task<ConnectionTestResult> TestConnectionAsync(ChannelContext ctx, CancellationToken ct)
    {
        using var res = await Send(ctx, HttpMethod.Get, $"properties/{ctx.RequirePropertyId()}", null, ct);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        var title = body.TryGetProperty("data", out var d) && d.TryGetProperty("attributes", out var a) && a.TryGetProperty("title", out var t)
            ? t.GetString() : null;
        return new ConnectionTestResult(true, $"Connected to Channex property '{title ?? ctx.Connection.ExternalPropertyId}'.");
    }

    public async Task PushAriAsync(ChannelContext ctx, AriSnapshot snapshot, CancellationToken ct)
    {
        var propertyId = ctx.RequirePropertyId();

        var availability = new List<object>();
        foreach (var room in snapshot.Availability.GroupBy(a => a.RoomTypeId))
        {
            var map = ctx.RoomMapping(room.Key);
            if (map is null) continue;
            availability.AddRange(AriRanges.Compress(room.Select(d => (d.Date, d.Available))).Select(r => new
            {
                property_id = propertyId, room_type_id = map.ExternalRoomId,
                date_from = Date(r.From), date_to = Date(r.ToInclusive), availability = r.Value,
            }));
        }

        var restrictions = new List<object>();
        foreach (var plan in snapshot.Rates.GroupBy(r => r.RatePlanId))
        {
            var map = ctx.RateMapping(plan.Key);
            if (map?.ExternalRateId is null) continue;
            restrictions.AddRange(AriRanges.Compress(plan.Select(d => (d.Date, d with { Date = default }))).Select(r => new
            {
                property_id = propertyId, rate_plan_id = map.ExternalRateId,
                date_from = Date(r.From), date_to = Date(r.ToInclusive),
                rate = Money(r.Value.Price),
                min_stay_arrival = r.Value.MinStay,
                stop_sell = r.Value.StopSell,
                closed_to_arrival = r.Value.ClosedToArrival,
                closed_to_departure = r.Value.ClosedToDeparture,
            }));
        }

        foreach (var chunk in availability.Chunk(1000))
            (await Send(ctx, HttpMethod.Post, "availability", new { values = chunk }, ct)).Dispose();
        foreach (var chunk in restrictions.Chunk(1000))
            (await Send(ctx, HttpMethod.Post, "restrictions", new { values = chunk }, ct)).Dispose();
    }

    public async Task<ReservationFetchResult> FetchReservationsAsync(ChannelContext ctx, CancellationToken ct)
    {
        using var res = await Send(ctx, HttpMethod.Get,
            $"booking_revisions/feed?filter[property_id]={Uri.EscapeDataString(ctx.RequirePropertyId())}", null, ct);
        var feed = await res.Content.ReadFromJsonAsync<ChannexFeed>(Json, ct);
        return new ReservationFetchResult(Map(feed).ToList());
    }

    public async Task AcknowledgeAsync(ChannelContext ctx, IReadOnlyList<ExternalReservation> processed, CancellationToken ct)
    {
        foreach (var revisionId in processed.Select(p => p.AckToken).OfType<string>().Distinct())
            (await Send(ctx, HttpMethod.Post, $"booking_revisions/{revisionId}/ack", null, ct)).Dispose();
    }

    internal static IEnumerable<ExternalReservation> Map(ChannexFeed? feed)
    {
        foreach (var item in feed?.Data ?? [])
        {
            var a = item.Attributes;
            var status = a.Status switch
            {
                "cancelled" => ExternalReservationStatus.Cancelled,
                "modified" => ExternalReservationStatus.Modified,
                _ => ExternalReservationStatus.New,
            };
            var guest = new GuestDto(
                a.Customer?.Name ?? "Guest", a.Customer?.Surname ?? (a.OtaName ?? "OTA"),
                a.Customer?.Mail ?? $"noreply+{a.BookingId}@channex.io", a.Customer?.Phone, a.Customer?.Country);
            var rooms = a.Rooms is { Count: > 0 } r ? r : [new ChannexRoom()];
            for (var i = 0; i < rooms.Count; i++)
            {
                var room = rooms[i];
                yield return new ExternalReservation(
                    ExternalId: rooms.Count > 1 ? $"{a.BookingId}-{i + 1}" : a.BookingId ?? item.Id,
                    Status: status,
                    ExternalRoomId: room.RoomTypeId ?? "",
                    ExternalRateId: room.RatePlanId,
                    CheckIn: ParseDate(room.CheckinDate ?? a.ArrivalDate),
                    CheckOut: ParseDate(room.CheckoutDate ?? a.DepartureDate),
                    Adults: room.Occupancy?.Adults ?? 1,
                    Children: (room.Occupancy?.Children ?? 0) + (room.Occupancy?.Infants ?? 0),
                    Guest: guest,
                    TotalAmount: ParseDecimal(rooms.Count > 1 ? room.Amount : a.Amount ?? room.Amount),
                    Currency: a.Currency ?? "EUR",
                    Commission: a.OtaCommission is null || rooms.Count > 1 ? null : ParseDecimal(a.OtaCommission),
                    Notes: string.IsNullOrWhiteSpace(a.Notes) ? null : $"[{a.OtaName} {a.OtaReservationCode}] {a.Notes}",
                    AckToken: item.Id,
                    Source: SourceFromOta(a.OtaName),
                    PaidToChannel: a.PaymentCollect == "ota");
            }
        }
    }

    internal static BookingSource SourceFromOta(string? ota) => ota?.ToLowerInvariant() switch
    {
        "bookingcom" or "booking.com" => BookingSource.BookingCom,
        "expedia" => BookingSource.Expedia,
        "airbnb" => BookingSource.Airbnb,
        "agoda" => BookingSource.Agoda,
        "hotelbeds" => BookingSource.Hotelbeds,
        _ => BookingSource.ChannelManager,
    };

    private async Task<HttpResponseMessage> Send(ChannelContext ctx, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        var req = new HttpRequestMessage(method, $"{ctx.SettingOrDefault("baseUrl", DefaultBaseUrl)!.TrimEnd('/')}/{path}");
        req.Headers.Add("user-api-key", ctx.RequireSecret());
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);
        var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            res.Dispose();
            throw new ChannelException($"Channex HTTP {(int)res.StatusCode}: {Truncate(text)}");
        }
        return res;
    }
}

internal record ChannexFeed(List<ChannexRevision>? Data);
internal record ChannexRevision(string Id, ChannexRevisionAttributes Attributes);
internal record ChannexRevisionAttributes(
    string? BookingId, string? Status, string? OtaName, string? OtaReservationCode, string? ArrivalDate, string? DepartureDate,
    string? Amount, string? Currency, string? OtaCommission, string? Notes, string? PaymentCollect,
    ChannexCustomer? Customer, List<ChannexRoom>? Rooms);
internal record ChannexCustomer(string? Name, string? Surname, string? Mail, string? Phone, string? Country);
internal record ChannexRoom(string? RoomTypeId = null, string? RatePlanId = null, string? CheckinDate = null, string? CheckoutDate = null,
    string? Amount = null, ChannexOccupancy? Occupancy = null);
internal record ChannexOccupancy(int Adults, int Children, int Infants);
