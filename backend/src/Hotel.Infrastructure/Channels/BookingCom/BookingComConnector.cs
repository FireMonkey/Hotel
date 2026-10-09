using System.Xml.Linq;
using Hotel.Application.Ari;
using Hotel.Application.Bookings;
using Hotel.Application.Channels;
using Hotel.Domain.Bookings;
using Hotel.Domain.Channels;
using static Hotel.Infrastructure.Channels.XmlChannelClient;

namespace Hotel.Infrastructure.Channels.BookingCom;

/// <summary>
/// Booking.com Connectivity API, OTA XML flavour.
///  • ARI:          OTA_HotelAvailNotif (rooms to sell + restrictions), OTA_HotelRateAmountNotif (prices)
///  • Reservations: OTA_HotelResNotif (new) and OTA_HotelResModifyNotif (modified / cancelled)
/// Direct access requires being a certified Booking.com Connectivity Partner; most single
/// properties connect through a channel manager instead (see ChannexConnector).
/// Endpoints are configurable so certification/test hosts can be used.
/// </summary>
public class BookingComConnector(IHttpClientFactory httpFactory) : IChannelConnector
{
    public const string HttpClientName = "bookingcom";
    private static readonly XNamespace Ota = "http://www.opentravel.org/OTA/2003/05";
    private const string DefaultSupplyUrl = "https://supply-xml.booking.com/hotels/ota";
    private const string DefaultSecureUrl = "https://secure-supply-xml.booking.com/hotels/ota";

    public ChannelType Type => ChannelType.BookingCom;

    public ChannelDescriptor Describe() => new(
        Type, "Booking.com",
        "Direct Booking.com Connectivity API (OTA XML). Requires Connectivity Partner credentials (machine account).",
        SupportsAriPush: true, SupportsReservationPull: true, RequiresPartnerCertification: true,
        [
            new("username", "Machine account username"),
            new("secret", "Machine account password", IsSecret: true),
            new("supplyUrl", "ARI endpoint base", Required: false, Placeholder: DefaultSupplyUrl),
            new("secureUrl", "Reservations endpoint base", Required: false, Placeholder: DefaultSecureUrl),
        ],
        "https://developers.booking.com/connectivity/docs");

    public async Task<ConnectionTestResult> TestConnectionAsync(ChannelContext ctx, CancellationToken ct)
    {
        // A reservations poll is side-effect free until reservations are confirmed.
        var doc = await GetReservationsXml(ctx, "OTA_HotelResNotif", ct);
        EnsureSuccess(doc);
        return new ConnectionTestResult(true, "Connected to Booking.com.");
    }

    public async Task PushAriAsync(ChannelContext ctx, AriSnapshot snapshot, CancellationToken ct)
    {
        var hotelCode = ctx.RequirePropertyId();
        var auth = Auth(ctx);
        var http = httpFactory.CreateClient(HttpClientName);
        var baseUrl = ctx.SettingOrDefault("supplyUrl", DefaultSupplyUrl)!.TrimEnd('/');

        // 1) Rooms to sell (per room type).
        var availMessages = new List<XElement>();
        foreach (var room in snapshot.Availability.GroupBy(a => a.RoomTypeId))
        {
            var map = ctx.RoomMapping(room.Key);
            if (map is null) continue;
            foreach (var r in AriRanges.Compress(room.Select(d => (d.Date, d.Available))))
                availMessages.Add(new XElement(Ota + "AvailStatusMessage", new XAttribute("BookingLimit", r.Value),
                    Control(r.From, r.ToInclusive, map.ExternalRoomId, null)));
        }

        // 2) Restrictions + 3) prices (per rate plan).
        var rateMessages = new List<XElement>();
        foreach (var plan in snapshot.Rates.GroupBy(r => r.RatePlanId))
        {
            var map = ctx.RateMapping(plan.Key);
            if (map?.ExternalRateId is null) continue;
            var restrictions = AriRanges.Compress(plan.Select(d => (d.Date, (d.MinStay, d.StopSell, d.ClosedToArrival, d.ClosedToDeparture))));
            foreach (var r in restrictions)
            {
                var (minStay, stopSell, cta, ctd) = r.Value;
                availMessages.Add(new XElement(Ota + "AvailStatusMessage",
                    Control(r.From, r.ToInclusive, map.ExternalRoomId, map.ExternalRateId),
                    new XElement(Ota + "LengthsOfStay", new XElement(Ota + "LengthOfStay",
                        new XAttribute("Time", minStay), new XAttribute("TimeUnit", "Day"), new XAttribute("MinMaxMessageType", "SetMinLOS"))),
                    new XElement(Ota + "RestrictionStatus", new XAttribute("Status", stopSell ? "Close" : "Open")),
                    new XElement(Ota + "RestrictionStatus", new XAttribute("Status", cta ? "Close" : "Open"), new XAttribute("Restriction", "Arrival")),
                    new XElement(Ota + "RestrictionStatus", new XAttribute("Status", ctd ? "Close" : "Open"), new XAttribute("Restriction", "Departure"))));
            }
            foreach (var r in AriRanges.Compress(plan.Select(d => (d.Date, (d.Price, d.Currency)))))
                rateMessages.Add(new XElement(Ota + "RateAmountMessage",
                    Control(r.From, r.ToInclusive, map.ExternalRoomId, map.ExternalRateId),
                    new XElement(Ota + "Rates", new XElement(Ota + "Rate", new XElement(Ota + "BaseByGuestAmts",
                        new XElement(Ota + "BaseByGuestAmt",
                            new XAttribute("AmountAfterTax", Money(r.Value.Price)),
                            new XAttribute("CurrencyCode", r.Value.Currency),
                            new XAttribute("DecimalPlaces", 2)))))));
            }

        foreach (var chunk in availMessages.Chunk(500))
        {
            var doc = Envelope("OTA_HotelAvailNotifRQ", new XElement(Ota + "AvailStatusMessages", new XAttribute("HotelCode", hotelCode), chunk));
            EnsureSuccess(await PostXmlAsync(http, $"{baseUrl}/OTA_HotelAvailNotif", doc, ct, auth));
        }
        foreach (var chunk in rateMessages.Chunk(500))
        {
            var doc = Envelope("OTA_HotelRateAmountNotifRQ", new XElement(Ota + "RateAmountMessages", new XAttribute("HotelCode", hotelCode), chunk));
            EnsureSuccess(await PostXmlAsync(http, $"{baseUrl}/OTA_HotelRateAmountNotif", doc, ct, auth));
        }
    }

    public async Task<ReservationFetchResult> FetchReservationsAsync(ChannelContext ctx, CancellationToken ct)
    {
        var list = new List<ExternalReservation>();
        foreach (var endpoint in new[] { "OTA_HotelResNotif", "OTA_HotelResModifyNotif" })
        {
            var doc = await GetReservationsXml(ctx, endpoint, ct);
            EnsureSuccess(doc);
            list.AddRange(ParseReservations(doc));
        }
        return new ReservationFetchResult(list);
    }

    public async Task AcknowledgeAsync(ChannelContext ctx, IReadOnlyList<ExternalReservation> processed, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        var baseUrl = ctx.SettingOrDefault("secureUrl", DefaultSecureUrl)!.TrimEnd('/');
        foreach (var group in processed.GroupBy(r => r.Status == ExternalReservationStatus.New))
        {
            var (endpoint, root) = group.Key
                ? ("OTA_HotelResNotif", "OTA_HotelResNotifRS")
                : ("OTA_HotelResModifyNotif", "OTA_HotelResModifyNotifRS");
            var reservations = group.Select(r => r.AckToken ?? r.ExternalId).Distinct().Select(id =>
                new XElement(Ota + "HotelReservation",
                    new XElement(Ota + "UniqueID", new XAttribute("Type", "14"), new XAttribute("ID", id))));
            var doc = Envelope(root, new XElement(Ota + "Success"),
                new XElement(Ota + (group.Key ? "HotelReservations" : "HotelResModifies"), reservations));
            EnsureSuccess(await PostXmlAsync(http, $"{baseUrl}/{endpoint}", doc, ct, Auth(ctx)));
        }
    }

    internal static IEnumerable<ExternalReservation> ParseReservations(XDocument doc)
    {
        var reservations = doc.Descendants(Ota + "HotelReservation").Concat(doc.Descendants(Ota + "HotelResModify"));
        foreach (var res in reservations)
        {
            var resId = res.Descendants(Ota + "HotelReservationID").FirstOrDefault()?.Attribute("ResID_Value")?.Value
                        ?? res.Element(Ota + "UniqueID")?.Attribute("ID")?.Value;
            if (resId is null) continue;

            var status = (res.Attribute("ResStatus")?.Value ?? "Commit") switch
            {
                "Cancel" or "Cancelled" => ExternalReservationStatus.Cancelled,
                "Modify" or "Modified" => ExternalReservationStatus.Modified,
                _ => res.Name.LocalName == "HotelResModify" ? ExternalReservationStatus.Modified : ExternalReservationStatus.New,
            };

            var customer = res.Descendants(Ota + "Customer").FirstOrDefault();
            var guest = new GuestDto(
                customer?.Descendants(Ota + "GivenName").FirstOrDefault()?.Value ?? "Booking.com",
                customer?.Descendants(Ota + "Surname").FirstOrDefault()?.Value ?? "Guest",
                customer?.Element(Ota + "Email")?.Value ?? "noreply@guest.booking.com",
                customer?.Element(Ota + "Telephone")?.Attribute("PhoneNumber")?.Value,
                customer?.Descendants(Ota + "CountryName").FirstOrDefault()?.Attribute("Code")?.Value);
            var comments = string.Join("\n", res.Descendants(Ota + "Comment").Select(c => c.Value.Trim()).Where(c => c.Length > 0));

            var stays = res.Descendants(Ota + "RoomStay").ToList();
            for (var i = 0; i < stays.Count; i++)
            {
                var stay = stays[i];
                var span = stay.Element(Ota + "TimeSpan");
                var counts = stay.Descendants(Ota + "GuestCount").ToList();
                int Count(string code) => counts.Where(c => c.Attribute("AgeQualifyingCode")?.Value == code)
                    .Sum(c => int.TryParse(c.Attribute("Count")?.Value, out var n) ? n : 0);
                var total = stay.Element(Ota + "Total");
                var commission = stay.Descendants(Ota + "Commission").FirstOrDefault()?.Element(Ota + "CommissionPayableAmount");

                yield return new ExternalReservation(
                    ExternalId: stays.Count > 1 ? $"{resId}-{i + 1}" : resId,
                    Status: status,
                    ExternalRoomId: stay.Descendants(Ota + "RoomType").FirstOrDefault()?.Attribute("RoomTypeCode")?.Value ?? "",
                    ExternalRateId: stay.Descendants(Ota + "RatePlan").FirstOrDefault()?.Attribute("RatePlanCode")?.Value,
                    CheckIn: ParseDate(span?.Attribute("Start")?.Value),
                    CheckOut: ParseDate(span?.Attribute("End")?.Value),
                    Adults: Math.Max(1, Count("10")),
                    Children: Count("8"),
                    Guest: guest,
                    TotalAmount: ParseDecimal(total?.Attribute("AmountAfterTax")?.Value),
                    Currency: total?.Attribute("CurrencyCode")?.Value ?? "EUR",
                    Commission: commission is null ? null : ParseDecimal(commission.Attribute("Amount")?.Value),
                    Notes: comments.Length > 0 ? comments : null,
                    AckToken: resId,
                    Source: BookingSource.BookingCom);
            }
        }
    }

    private async Task<XDocument> GetReservationsXml(ChannelContext ctx, string endpoint, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        var baseUrl = ctx.SettingOrDefault("secureUrl", DefaultSecureUrl)!.TrimEnd('/');
        var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/{endpoint}?hotel_ids={Uri.EscapeDataString(ctx.RequirePropertyId())}");
        req.Headers.Authorization = Auth(ctx);
        return await SendAsync(http, req, ct);
    }

    private static System.Net.Http.Headers.AuthenticationHeaderValue Auth(ChannelContext ctx) =>
        Basic(ctx.Setting("username"), ctx.RequireSecret());

    private static XElement Control(DateOnly from, DateOnly to, string room, string? rate)
    {
        var e = new XElement(Ota + "StatusApplicationControl",
            new XAttribute("Start", Date(from)), new XAttribute("End", Date(to)), new XAttribute("InvTypeCode", room));
        if (rate is not null) e.Add(new XAttribute("RatePlanCode", rate));
        return e;
    }

    private static XDocument Envelope(string root, params object[] content) =>
        new(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ota + root,
                new XAttribute("TimeStamp", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssK")),
                new XAttribute("Version", "3.0"),
                new XAttribute("EchoToken", Guid.NewGuid().ToString("N")),
                content));

    private static void EnsureSuccess(XDocument doc)
    {
        var errors = doc.Descendants(Ota + "Error").Concat(doc.Descendants("error")).ToList();
        if (errors.Count > 0)
            throw new ChannelException("Booking.com: " + string.Join("; ", errors.Select(e => e.Attribute("ShortText")?.Value ?? e.Value)));
    }
}
