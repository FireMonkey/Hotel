using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Hotel.Application.Channels;

namespace Hotel.Infrastructure.Channels;

/// <summary>Small helper shared by the XML based OTA connectors (Booking.com, Expedia).</summary>
public static class XmlChannelClient
{
    public static async Task<XDocument> PostXmlAsync(HttpClient http, string url, XDocument body, CancellationToken ct,
        AuthenticationHeaderValue? auth = null)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.Declaration + body.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml"),
        };
        if (auth is not null) req.Headers.Authorization = auth;
        return await SendAsync(http, req, ct);
    }

    public static async Task<XDocument> SendAsync(HttpClient http, HttpRequestMessage req, CancellationToken ct)
    {
        using var res = await http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
            throw new ChannelException($"HTTP {(int)res.StatusCode}: {Truncate(text)}");
        try { return XDocument.Parse(text); }
        catch (System.Xml.XmlException ex) { throw new ChannelException($"Invalid XML response: {Truncate(text)}", ex); }
    }

    public static AuthenticationHeaderValue Basic(string user, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));

    public static string Truncate(string s, int max = 500) => s.Length <= max ? s : s[..max] + "…";

    public static string Date(DateOnly d) => d.ToString("yyyy-MM-dd");

    public static string Money(decimal m) => m.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    public static DateOnly ParseDate(string? s) =>
        DateOnly.TryParse(s?[..Math.Min(10, s.Length)], System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : throw new ChannelException($"Invalid date '{s}'.");

    public static decimal ParseDecimal(string? s) =>
        decimal.TryParse(s, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0m;
}
