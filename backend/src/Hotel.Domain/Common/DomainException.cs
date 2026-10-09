namespace Hotel.Domain.Common;

/// <summary>Business rule violation. Mapped to HTTP 409/422 by the API layer.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
