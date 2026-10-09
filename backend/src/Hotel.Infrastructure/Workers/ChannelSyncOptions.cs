namespace Hotel.Infrastructure.Workers;

public class ChannelSyncOptions
{
    public const string Section = "ChannelSync";
    public bool Enabled { get; set; } = true;
    /// <summary>How often queued ARI changes are pushed.</summary>
    public int AriPushIntervalSeconds { get; set; } = 15;
    /// <summary>How often reservations are pulled from channels.</summary>
    public int ReservationPullIntervalSeconds { get; set; } = 120;
}
