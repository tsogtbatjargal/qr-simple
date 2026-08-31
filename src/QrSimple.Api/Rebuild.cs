namespace QrSimple.Api;

// A rebuild is a rare, multi-year event, unlike the periodic inspections this entity started
// life as (docs/plans/0002-inspection-records.md) — Note and RebuildDate are the record, and
// the PDF is supporting evidence that may not exist yet when the record is filed. Hence
// Content/ContentType/FileName are nullable and Note is not: the opposite of the original
// design, where the PDF *was* the record. Every render site must branch on Content is null.
public class Rebuild
{
    public Guid Id { get; set; }
    public required Guid EquipmentId { get; set; }
    public required DateOnly RebuildDate { get; set; } // operator-entered, not upload time
    public required string Note { get; set; }
    public byte[]? Content { get; set; } // null until a PDF is attached; see RebuildCatalog.AttachFileAsync
    public string? ContentType { get; set; }
    public string? FileName { get; set; } // as uploaded; not what's served, see RebuildCatalog
    public required string UploadedByEmail { get; set; } // admin-visible only, never rendered publicly
    public required DateTimeOffset UploadedAtUtc { get; set; }
    public DateTimeOffset? LastEditedAtUtc { get; set; } // null until first edit
    public string? LastEditedByEmail { get; set; }
}
