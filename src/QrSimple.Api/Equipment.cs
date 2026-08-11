namespace QrSimple.Api;

public enum EquipmentStatus
{
    Active,
    Retired,
}

public class Equipment
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public required string SerialNumber { get; set; }
    public required string Site { get; set; }
    public EquipmentStatus Status { get; set; } = EquipmentStatus.Active;
}
