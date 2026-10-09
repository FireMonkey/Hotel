using Hotel.Domain.Common;

namespace Hotel.Domain.Channels;

public class ChannelConnection : Entity
{
    public ChannelType Channel { get; set; }
    public required string Name { get; set; }
    public bool IsEnabled { get; set; }
    /// <summary>Property / hotel id on the channel side.</summary>
    public string? ExternalPropertyId { get; set; }
    /// <summary>Provider specific settings (endpoint, username …). Secrets are stored encrypted.</summary>
    public Dictionary<string, string> Settings { get; set; } = [];
    public string? EncryptedSecret { get; set; }
    /// <summary>Commission % used to compute net revenue for this channel.</summary>
    public decimal CommissionPercent { get; set; }
    /// <summary>Markup applied to rates pushed to this channel (e.g. 15 = +15%).</summary>
    public decimal RateMarkupPercent { get; set; }
    public DateTimeOffset? LastReservationPullAt { get; set; }
    public DateTimeOffset? LastAriPushAt { get; set; }
    public string? LastError { get; set; }

    public List<ChannelMapping> Mappings { get; set; } = [];
}

/// <summary>Maps a local room type / rate plan to the channel's room / rate identifiers.</summary>
public class ChannelMapping : Entity
{
    public Guid ChannelConnectionId { get; set; }
    public Guid RoomTypeId { get; set; }
    public Guid? RatePlanId { get; set; }
    public required string ExternalRoomId { get; set; }
    public string? ExternalRateId { get; set; }
    /// <summary>iCal: URL of the remote calendar to import.</summary>
    public string? ImportUrl { get; set; }
}
