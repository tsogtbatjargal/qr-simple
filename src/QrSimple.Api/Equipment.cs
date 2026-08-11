namespace QrSimple.Api;

public class Equipment
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public required string SerialNumber { get; set; }
    public required string Site { get; set; }
    public string Status { get; set; } = "Active";
}
