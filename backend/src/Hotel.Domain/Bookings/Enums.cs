namespace Hotel.Domain.Bookings;

public enum BookingStatus { Pending = 0, Confirmed = 1, CheckedIn = 2, CheckedOut = 3, Cancelled = 4, NoShow = 5 }

public enum PaymentStatus { Unpaid = 0, PartiallyPaid = 1, Paid = 2, Refunded = 3, PaidToChannel = 4 }

/// <summary>Where the reservation came from. Matches ChannelType for OTA-originated bookings.</summary>
public enum BookingSource { Direct = 0, Admin = 1, BookingCom = 10, Expedia = 11, Airbnb = 12, Agoda = 13, Hotelbeds = 14, ChannelManager = 50, ICal = 60 }
