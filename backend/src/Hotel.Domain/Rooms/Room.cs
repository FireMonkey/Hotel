using Hotel.Domain.Common;

namespace Hotel.Domain.Rooms;

public enum RoomStatus { Available = 0, OutOfOrder = 1, Maintenance = 2 }

public enum HousekeepingStatus { Clean = 0, Dirty = 1, Inspected = 2 }

public class Room : Entity
{
    public required string Number { get; set; }
    public int Floor { get; set; }
    public Guid RoomTypeId { get; set; }
    public RoomType? RoomType { get; set; }
    public RoomStatus Status { get; set; } = RoomStatus.Available;
    public HousekeepingStatus Housekeeping { get; set; } = HousekeepingStatus.Clean;
    public string? Notes { get; set; }
}
