namespace Hotel.Infrastructure.Channels;

public record DateRange<T>(DateOnly From, DateOnly ToInclusive, T Value);

public static class AriRanges
{
    /// <summary>
    /// Collapses consecutive days with equal values into ranges, which keeps OTA
    /// payloads small (one message per price/restriction change instead of per day).
    /// </summary>
    public static IEnumerable<DateRange<T>> Compress<T>(IEnumerable<(DateOnly Date, T Value)> days)
    {
        DateRange<T>? current = null;
        foreach (var (date, value) in days.OrderBy(d => d.Date))
        {
            if (current is not null && current.ToInclusive.AddDays(1) == date && EqualityComparer<T>.Default.Equals(current.Value, value))
            {
                current = current with { ToInclusive = date };
                continue;
            }
            if (current is not null) yield return current;
            current = new DateRange<T>(date, date, value);
        }
        if (current is not null) yield return current;
    }
}
