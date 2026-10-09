using System.Security.Cryptography;
using Hotel.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Hotel.Infrastructure.Services;

/// <summary>Encrypts channel credentials at rest with ASP.NET Core Data Protection.</summary>
public class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Hotel.ChannelSecrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText)) return null;
        try { return _protector.Unprotect(protectedText); }
        catch (CryptographicException) { return null; } // key ring changed → treat as not configured
    }
}
