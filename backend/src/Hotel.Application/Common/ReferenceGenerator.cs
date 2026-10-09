using System.Security.Cryptography;

namespace Hotel.Application.Common;

public static class ReferenceGenerator
{
    // No ambiguous chars (0/O, 1/I).
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string NewBookingReference() =>
        "HB" + RandomNumberGenerator.GetString(Alphabet, 8);

    public static string NewToken(int length = 32) =>
        RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyz0123456789", length);
}
