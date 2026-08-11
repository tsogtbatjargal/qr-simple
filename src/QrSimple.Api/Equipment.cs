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

    public static Equipment Create(string name, string category, string serialNumber, string site) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Category = category,
        SerialNumber = serialNumber,
        Site = site,
    };
}
