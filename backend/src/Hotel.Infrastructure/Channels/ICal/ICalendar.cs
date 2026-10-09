using System.Globalization;
using System.Text;

namespace Hotel.Infrastructure.Channels.ICal;

public record ICalEvent(string Uid, DateOnly Start, DateOnly End, string? Summary, bool Cancelled);

/// <summary>Minimal RFC 5545 reader/writer — enough for OTA availability calendars.</summary>
public static class ICalendar
{
    public static IReadOnlyList<ICalEvent> Parse(string ics)
    {
        var events = new List<ICalEvent>();
        Dictionary<string, string>? current = null;
        foreach (var line in Unfold(ics))
        {
            if (line == "BEGIN:VEVENT") { current = new(StringComparer.OrdinalIgnoreCase); continue; }
            if (line == "END:VEVENT" && current is not null)
            {
                if (current.TryGetValue("DTSTART", out var s) && TryDate(s, out var start))
                {
                    var end = current.TryGetValue("DTEND", out var e) && TryDate(e, out var parsedEnd) ? parsedEnd : start.AddDays(1);
                    if (end <= start) end = start.AddDays(1);
                    var uid = current.GetValueOrDefault("UID") ?? $"{start:yyyyMMdd}-{end:yyyyMMdd}";
                    events.Add(new ICalEvent(uid, start, end, current.GetValueOrDefault("SUMMARY"),
                        string.Equals(current.GetValueOrDefault("STATUS"), "CANCELLED", StringComparison.OrdinalIgnoreCase)));
                }
                current = null;
                continue;
            }
            if (current is null) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var name = line[..colon].Split(';')[0];
            current[name] = line[(colon + 1)..];
        }
        return events;
    }

    public static string Write(string productId, string calendarName, IEnumerable<ICalEvent> events)
    {
        var sb = new StringBuilder();
        void L(string s) => sb.Append(s).Append("\r\n");
        L("BEGIN:VCALENDAR");
        L("VERSION:2.0");
        L($"PRODID:-//{productId}//EN");
        L("CALSCALE:GREGORIAN");
        L("METHOD:PUBLISH");
        L($"X-WR-CALNAME:{Escape(calendarName)}");
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        foreach (var e in events)
        {
            L("BEGIN:VEVENT");
            L($"UID:{e.Uid}");
            L($"DTSTAMP:{stamp}");
            L($"DTSTART;VALUE=DATE:{e.Start:yyyyMMdd}");
            L($"DTEND;VALUE=DATE:{e.End:yyyyMMdd}");
            L($"SUMMARY:{Escape(e.Summary ?? "Not available")}");
            L("END:VEVENT");
        }
        L("END:VCALENDAR");
        return sb.ToString();
    }

    private static IEnumerable<string> Unfold(string ics)
    {
        var lines = ics.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        foreach (var raw in lines)
        {
            if ((raw.StartsWith(' ') || raw.StartsWith('\t')) && sb.Length > 0) { sb.Append(raw[1..]); continue; }
            if (sb.Length > 0) yield return sb.ToString().Trim();
            sb.Clear().Append(raw);
        }
        if (sb.Length > 0) yield return sb.ToString().Trim();
    }

    private static bool TryDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value.Length >= 8 ? value[..8] : value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace(",", "\\,").Replace(";", "\\;").Replace("\n", "\\n");
}
