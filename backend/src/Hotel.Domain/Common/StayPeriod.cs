namespace Hotel.Domain.Common;

/// <summary>Half-open interval [CheckIn, CheckOut) measured in nights.</summary>
public readonly record struct StayPeriod
{
    public DateOnly CheckIn { get; }
    public DateOnly CheckOut { get; }

    public StayPeriod(DateOnly checkIn, DateOnly checkOut)
    {
        if (checkOut <= checkIn)
            throw new DomainException("invalid_stay", "Check-out must be after check-in.");
        CheckIn = checkIn;
        CheckOut = checkOut;
    }

    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;

    public IEnumerable<DateOnly> Dates()
    {
        for (var d = CheckIn; d < CheckOut; d = d.AddDays(1))
            yield return d;
    }

    public bool Overlaps(DateOnly checkIn, DateOnly checkOut) => CheckIn < checkOut && checkIn < CheckOut;
}
