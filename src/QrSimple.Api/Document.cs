namespace QrSimple.Api;

public class Document
{
    public Guid Id { get; set; }
    public required Guid EquipmentId { get; set; }
    public required string Label { get; set; }
    public required string Url { get; set; }
}
