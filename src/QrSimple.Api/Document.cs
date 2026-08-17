namespace QrSimple.Api;

public class Document
{
    public Guid Id { get; set; }
    public required Guid EquipmentId { get; set; }
    public required string Label { get; set; }
    public string? Url { get; set; }
    public byte[]? Content { get; set; }
    public string? ContentType { get; set; }
    public string? FileName { get; set; }
}
