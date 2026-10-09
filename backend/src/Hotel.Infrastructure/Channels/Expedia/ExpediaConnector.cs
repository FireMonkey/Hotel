using System.Xml.Linq;
using Hotel.Application.Ari;
using Hotel.Application.Bookings;
using Hotel.Application.Channels;
using Hotel.Domain.Bookings;
using Hotel.Domain.Channels;
using static Hotel.Infrastructure.Channels.XmlChannelClient;

namespace Hotel.Infrastructure.Channels.Expedia;

/// <summary>
/// Expedia QuickConnect (EQC): AR (availability &amp; rates), BR (booking retrieval), BC (booking confirmation).
/// Requires EQC credentials issued by Expedia to a connectivity provider. Covers Expedia, Hotels.com, Vrbo hotel inventory.
/// </summary>
public class ExpediaConnector(IHttpClientFactory httpFactory) : IChannelConnector
{
    public const string HttpClientName = "expedia";
    private const string DefaultBaseUrl = "https://services.expediapartnercentral.com/eqc";
    private static readonly XNamespace Ar = "http://www.expediaconnect.com/EQC/AR/2011/06";
    private static readonly XNamespace Br = "http://www.expediaconnect.com/EQC/BR/2014/01";
    private static readonly XNamespace Bc = "http://www.expediaconnect.com/EQC/BC/2007/09";

    public ChannelType Type => ChannelType.Expedia;

    public ChannelDescriptor Describe() => new(
        Type, "Expedia Group",
        "Expedia QuickConnect (EQC) — Expedia, Hotels.com, Vrbo. Requires EQC API credentials.",
        SupportsAriPush: true, SupportsReservationPull: true, RequiresPartnerCertification: true,
        [
            new("username", "EQC username"),
            new("secret", "EQC password", IsSecret: true),
            new("baseUrl", "EQC base URL", Required: false, Placeholder: DefaultBaseUrl),
        ],
        "https://developers.expediagroup.com/supply/lodging");

    public async Task<ConnectionTestResult> TestConnectionAsync(ChannelContext ctx, CancellationToken ct)
    {
        var doc = await Post(ctx, "br", BookingRetrieval(ctx, onlyPending: true), ct);
        EnsureSuccess(doc);
        return new ConnectionTestResult(true, "Connected to Expedia QuickConnect.");
    }

    public async Task PushAriAsync(ChannelContext ctx, AriSnapshot snapshot, CancellationToken ct)
    {
        var updates = new List<XElement>();
        foreach (var room in snapshot.Availability.GroupBy(a => a.RoomTypeId))
        {
            var map = ctx.RoomMapping(room.Key);
            if (map is null) continue;
            foreach (var r in AriRanges.Compress(room.Select(d => (d.Date, d.Available))))
                updates.Add(new XElement(Ar + "AvailRateUpdate", Range(r.From, r.ToInclusive),
                    new XElement(Ar + "RoomType", new XAttribute("id", map.ExternalRoomId),
                        new XElement(Ar + "Inventory", new XAttribute("totalInventoryAvailable", r.Value)))));
        }
        foreach (var plan in snapshot.Rates.GroupBy(r => r.RatePlanId))
        {
            var map = ctx.RateMapping(plan.Key);
            if (map?.ExternalRateId is null) continue;
            foreach (var r in AriRanges.Compress(plan.Select(d => (d.Date, d with { Date = default }))))
            {
                var v = r.Value;
                updates.Add(new XElement(Ar + "AvailRateUpdate", Range(r.From, r.ToInclusive),
                    new XElement(Ar + "RoomType", new XAttribute("id", map.ExternalRoomId),
                        new XElement(Ar + "RatePlan", new XAttribute("id", map.ExternalRateId), new XAttribute("closed", Bool(v.StopSell)),
                            new XElement(Ar + "Rate", new XAttribute("currency", v.Currency),
                                new XElement(Ar + "PerDay", new XAttribute("rate", Money(v.Price)))),
                            new XElement(Ar + "Restrictions",
                                new XAttribute("minLOS", v.MinStay),
                                new XAttribute("closedToArrival", Bool(v.ClosedToArrival)),
                                new XAttribute("closedToDeparture", Bool(v.ClosedToDeparture)))))));
            }
        }

        foreach (var chunk in updates.Chunk(200))
        {
            var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
                new XElement(Ar + "AvailRateUpdateRQ", AuthElement(Ar, ctx), new XElement(Ar + "Hotel", new XAttribute("id", ctx.RequirePropertyId())), chunk));
            EnsureSuccess(await Post(ctx, "ar", doc, ct));
        }
    }

    public async Task<ReservationFetchResult> FetchReservationsAsync(ChannelContext ctx, CancellationToken ct)
    {
        var doc = await Post(ctx, "br", BookingRetrieval(ctx, onlyPending: true), ct);
        EnsureSuccess(doc);
        return new ReservationFetchResult(ParseBookings(doc).ToList());
    }

    public async Task AcknowledgeAsync(ChannelContext ctx, IReadOnlyList<ExternalReservation> processed, CancellationToken ct)
    {
        var numbers = processed.Select(r => new XElement(Bc + "BookingConfirmNumber",
            new XAttribute("bookingID", r.ExternalId),
            new XAttribute("bookingType", r.AckToken ?? "Book"),
            new XAttribute("confirmNumber", r.ExternalId),
            new XAttribute("confirmTime", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssK"))));
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Bc + "BookingConfirmRQ", AuthElement(Bc, ctx),
                new XElement(Bc + "Hotel", new XAttribute("id", ctx.RequirePropertyId())),
                new XElement(Bc + "BookingConfirmNumbers", numbers)));
        EnsureSuccess(await Post(ctx, "bc", doc, ct));
    }

    internal static IEnumerable<ExternalReservation> ParseBookings(XDocument doc)
    {
        foreach (var b in doc.Descendants(Br + "Booking"))
        {
            var type = b.Attribute("type")?.Value ?? "Book";
            var stay = b.Element(Br + "RoomStay");
            if (stay is null) continue;
            var dates = stay.Element(Br + "StayDate");
            var count = stay.Element(Br + "GuestCount");
            var total = stay.Element(Br + "Total");
            var guest = b.Element(Br + "PrimaryGuest");
            var name = guest?.Element(Br + "Name");
            var phone = guest?.Element(Br + "Phone");

            yield return new ExternalReservation(
                ExternalId: b.Attribute("id")?.Value ?? throw new ChannelException("Expedia booking without id."),
                Status: type switch
                {
                    "Cancel" => ExternalReservationStatus.Cancelled,
                    "Modify" => ExternalReservationStatus.Modified,
                    _ => ExternalReservationStatus.New,
                },
                ExternalRoomId: stay.Attribute("roomTypeID")?.Value ?? "",
                ExternalRateId: stay.Attribute("ratePlanID")?.Value,
                CheckIn: ParseDate(dates?.Attribute("arrival")?.Value),
                CheckOut: ParseDate(dates?.Attribute("departure")?.Value),
                Adults: int.TryParse(count?.Attribute("adult")?.Value, out var a) ? a : 1,
                Children: int.TryParse(count?.Attribute("child")?.Value, out var c) ? c : 0,
                Guest: new GuestDto(
                    name?.Attribute("givenName")?.Value ?? "Expedia",
                    name?.Attribute("surname")?.Value ?? "Guest",
                    guest?.Element(Br + "Email")?.Value ?? "noreply@guest.expedia.com",
                    phone is null ? null : $"+{phone.Attribute("countryCode")?.Value}{phone.Attribute("cityAreaCode")?.Value}{phone.Attribute("number")?.Value}",
                    null),
                TotalAmount: ParseDecimal(total?.Attribute("amountAfterTaxes")?.Value),
                Currency: total?.Attribute("currency")?.Value ?? "EUR",
                Notes: b.Element(Br + "SpecialRequest")?.Value,
                AckToken: type,
                Source: BookingSource.Expedia);
        }
    }

    private static XDocument BookingRetrieval(ChannelContext ctx, bool onlyPending) =>
        new(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Br + "BookingRetrievalRQ", AuthElement(Br, ctx),
                new XElement(Br + "Hotel", new XAttribute("id", ctx.RequirePropertyId())),
                onlyPending ? new XElement(Br + "ParamSet", new XElement(Br + "Status", new XAttribute("value", "pending"))) : null));

    private Task<XDocument> Post(ChannelContext ctx, string service, XDocument doc, CancellationToken ct) =>
        PostXmlAsync(httpFactory.CreateClient(HttpClientName), $"{ctx.SettingOrDefault("baseUrl", DefaultBaseUrl)!.TrimEnd('/')}/{service}", doc, ct);

    private static XElement AuthElement(XNamespace ns, ChannelContext ctx) =>
        new(ns + "Authentication", new XAttribute("username", ctx.Setting("username")), new XAttribute("password", ctx.RequireSecret()));

    private static XElement Range(DateOnly from, DateOnly to) =>
        new(Ar + "DateRange", new XAttribute("from", Date(from)), new XAttribute("to", Date(to)));

    private static string Bool(bool b) => b ? "true" : "false";

    private static void EnsureSuccess(XDocument doc)
    {
        var errors = doc.Descendants().Where(e => e.Name.LocalName == "Error").ToList();
        if (errors.Count > 0)
            throw new ChannelException("Expedia: " + string.Join("; ", errors.Select(e => $"{e.Attribute("code")?.Value} {e.Value}".Trim())));
    }
}
