using Hotel.Application.Abstractions;
using Hotel.Domain.Channels;

namespace Hotel.Application.Ari;

public static class AriOutbox
{
    /// <summary>Queue an ARI push for the range [from, to). Saved with the caller's SaveChanges.</summary>
    public static void Enqueue(IAppDbContext db, Guid roomTypeId, DateOnly from, DateOnly to)
    {
        if (to <= from) to = from.AddDays(1);
        db.AriOutbox.Add(new AriOutboxMessage { RoomTypeId = roomTypeId, From = from, To = to });
    }
}
