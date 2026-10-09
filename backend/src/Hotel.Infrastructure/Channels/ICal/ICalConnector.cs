using Hotel.Application.Ari;
using Hotel.Application.Bookings;
using Hotel.Application.Channels;
using Hotel.Domain.Bookings;
using Hotel.Domain.Channels;

namespace Hotel.Infrastructure.Channels.ICal;

/// <summary>
/// iCal sync — works with Airbnb, Vrbo, Google Calendar and most OTAs' "calendar sync" without
/// any partnership. Import: each mapping's ImportUrl is polled and events become blocking bookings.
/// Export: /api/ical/{connectionId}/{roomTypeId}.ics?token=… serves dates with no free rooms.
/// iCal carries no prices or guest data, so it is a fallback, not a full integration.
/// </summary>
public class ICalConnector(IHttpClientFactory httpFactory) : IChannelConnector
{
    public const string HttpClientName = "ical";
    public const string ExportTokenSetting = "exportToken";

    public ChannelType Type => ChannelType.ICal;

    public ChannelDescriptor Describe() => new(
        Type, "iCal (Airbnb, Vrbo…)",
        "Calendar sync. Blocks dates in both directions; no rates or guest details. Add the import URL per room on the mapping.",
        SupportsAriPush: false, SupportsReservationPull: true, RequiresPartnerCertification: false,
        [new("source", "Source label (e.g. Airbnb)", Required: false, Placeholder: "Airbnb")],
        null);

    public async Task<ConnectionTestResult> TestConnectionAsync(ChannelContext ctx, CancellationToken ct)
    {
        var urls = ctx.Mappings.Where(m => !string.IsNullOrWhiteSpace(m.ImportUrl)).ToList();
        if (urls.Count == 0) return new ConnectionTestResult(true, "No import URLs configured; export feeds are active.");
        var total = 0;
        foreach (var m in urls) total += (await Download(m.ImportUrl!, ct)).Count;
        return new ConnectionTestResult(true, $"Read {urls.Count} calendar(s), {total} event(s).");
    }

    public Task PushAriAsync(ChannelContext ctx, AriSnapshot snapshot, CancellationToken ct) => Task.CompletedTask;

    public async Task<ReservationFetchResult> FetchReservationsAsync(ChannelContext ctx, CancellationToken ct)
    {
        var label = ctx.SettingOrDefault("source", ctx.Connection.Name)!;
        var source = label.Contains("airbnb", StringComparison.OrdinalIgnoreCase) ? BookingSource.Airbnb : BookingSource.ICal;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var list = new List<ExternalReservation>();
        foreach (var m in ctx.Mappings.Where(m => !string.IsNullOrWhiteSpace(m.ImportUrl)))
        {
            foreach (var e in await Download(m.ImportUrl!, ct))
            {
                if (e.Cancelled || e.End <= today) continue;
                list.Add(new ExternalReservation(
                    ExternalId: $"{m.ExternalRoomId}:{e.Uid}"[..Math.Min(100, $"{m.ExternalRoomId}:{e.Uid}".Length)],
                    Status: ExternalReservationStatus.New,
                    ExternalRoomId: m.ExternalRoomId,
                    ExternalRateId: m.ExternalRateId,
                    CheckIn: e.Start, CheckOut: e.End, Adults: 1, Children: 0,
                    Guest: new GuestDto(label, "Guest", "noreply@ical.local", null, null),
                    TotalAmount: 0, Currency: "TRY",
                    Notes: e.Summary,
                    Source: source,
                    PaidToChannel: true));
            }
        }
        return new ReservationFetchResult(list, IsFullSnapshot: true);
    }

    public Task AcknowledgeAsync(ChannelContext ctx, IReadOnlyList<ExternalReservation> processed, CancellationToken ct) => Task.CompletedTask;

    private async Task<IReadOnlyList<ICalEvent>> Download(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new ChannelException($"Invalid calendar URL '{url}'.");
        var http = httpFactory.CreateClient(HttpClientName);
        using var res = await http.GetAsync(uri, ct);
        if (!res.IsSuccessStatusCode) throw new ChannelException($"Calendar {uri.Host} returned HTTP {(int)res.StatusCode}.");
        return ICalendar.Parse(await res.Content.ReadAsStringAsync(ct));
    }
}
