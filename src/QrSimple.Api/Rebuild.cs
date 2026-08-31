namespace QrSimple.Api;

// Content is deliberately non-nullable, unlike Document.Content/Url (see AGENTS.md's
// DocumentCatalog bullet and docs/plans/0002-inspection-records.md decision 2) — there are no
// legacy inspection rows, so every render/read site can assume bytes exist and skip the
// Content-vs-Url branch that Document requires everywhere.
public class Inspection
{
    public Guid Id { get; set; }
    public required Guid EquipmentId { get; set; }
    public required string Kind { get; set; } // one of InspectionKinds.All
    public required DateOnly InspectionDate { get; set; } // operator-entered, not upload time
    public string? Note { get; set; }
    public required byte[] Content { get; set; }
    public required string ContentType { get; set; }
    public required string FileName { get; set; } // as uploaded; not what's served, see InspectionCatalog
    public required string UploadedByEmail { get; set; } // admin-visible only, never rendered publicly
    public required DateTimeOffset UploadedAtUtc { get; set; }
    public DateTimeOffset? LastEditedAtUtc { get; set; } // null until first edit
    public string? LastEditedByEmail { get; set; }
}
