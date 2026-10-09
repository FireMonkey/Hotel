namespace Hotel.Domain.Rooms;

/// <summary>
/// Optional cap on how many rooms of a type are sellable on a date
/// (e.g. hold back rooms from sale). Null-row = all physical rooms sellable.
/// </summary>
public class InventoryOverride
{
    public Guid RoomTypeId { get; set; }
    public DateOnly Date { get; set; }
    public int MaxSellable { get; set; }
}
