namespace Hotel.Domain.Channels;

public enum ChannelType
{
    /// <summary>Booking.com Connectivity API (OTA XML). Requires Connectivity Partner certification.</summary>
    BookingCom = 10,
    /// <summary>Expedia QuickConnect (EQC) — AR + Booking Retrieval. Requires Expedia connectivity approval.</summary>
    Expedia = 11,
    /// <summary>Channex.io channel manager — one integration, 50+ OTAs (Booking.com, Expedia, Airbnb, Agoda…).</summary>
    Channex = 50,
    /// <summary>iCal feed import/export (Airbnb, VRBO, Google…). Availability blocking only, no rates.</summary>
    ICal = 60,
}
