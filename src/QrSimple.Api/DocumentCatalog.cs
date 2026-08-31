using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api;

public abstract record DocumentResult
{
    public sealed record Success(Document Document) : DocumentResult;
    public sealed record NotFound : DocumentResult;
    public sealed record EquipmentNotFound : DocumentResult;
    public sealed record InvalidFile(string Reason) : DocumentResult;

    public IResult ToHttpResult(Func<Document, IResult> onSuccess) => this switch
    {
        Success s => onSuccess(s.Document),
        NotFound => Results.NotFound(),
        EquipmentNotFound => Results.NotFound(),
        InvalidFile invalid => Results.BadRequest(new { error = invalid.Reason }),
        _ => Results.Problem(),
    };
}

public enum UploadKind { Photo, Document, Rebuild, OemReport }

public static class DocumentUpload
{
    public const long MaxPhotoBytes = 5 * 1024 * 1024;
    public const long MaxDocumentBytes = 20 * 1024 * 1024;
    public const long MaxRebuildBytes = 10 * 1024 * 1024;

    public static readonly string[] PhotoExtensions = [".jpg", ".jpeg", ".png", ".webp"];
    public static readonly string[] PhotoContentTypes = ["image/jpeg", "image/png", "image/webp"];

    public static readonly string[] DocumentExtensions = [".pdf", ".doc", ".docx", ".xls", ".xlsx"];
    public static readonly string[] DocumentContentTypes =
    [
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    ];

    // Rebuild records and the OEM QA/QC report accept only PDF, unlike Documents
    // (.pdf/.doc/.docx/.xls/.xlsx) — both are signed-off artifacts, and an editable .docx
    // invites "which copy is the real one." See docs/plans/0002-inspection-records.md decision 4.
    public static readonly string[] PdfOnlyExtensions = [".pdf"];
    public static readonly string[] PdfOnlyContentTypes = ["application/pdf"];

    // Extension + browser-reported ContentType only, no magic-byte sniffing — internal tool,
    // trusted Admin/Operator roles (see docs/plans/0001-document-file-upload.md decision #4).
    public static string? Validate(string fileName, string contentType, long sizeBytes, UploadKind kind)
    {
        var (extensions, contentTypes, maxBytes, label) = kind switch
        {
            UploadKind.Photo => (PhotoExtensions, PhotoContentTypes, MaxPhotoBytes, "photo"),
            UploadKind.Rebuild => (PdfOnlyExtensions, PdfOnlyContentTypes, MaxRebuildBytes, "rebuild record"),
            UploadKind.OemReport => (PdfOnlyExtensions, PdfOnlyContentTypes, MaxDocumentBytes, "OEM QA/QC report"),
            _ => (DocumentExtensions, DocumentContentTypes, MaxDocumentBytes, "document"),
        };

        if (sizeBytes <= 0)
        {
            return "File is empty.";
        }

        if (sizeBytes > maxBytes)
        {
            return $"File is too large. Maximum size for a {label} is {maxBytes / (1024 * 1024)}MB.";
        }

        var extension = Path.GetExtension(fileName);
        if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
            !contentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return $"Unsupported file type for a {label}. Allowed: {string.Join(", ", extensions)}.";
        }

        return null;
    }
}

public static class DocumentCatalog
{
    // ScanPage.cs picks the header photo with FirstOrDefault against these labels, so if more
    // than one existed for the same equipment the "winner" would depend on undefined DB row
    // order. SetPhotoUploadAsync is the only path that keeps that invariant (one photo per
    // equipment) by overwriting the existing photo row instead of inserting a second one; the
    // same holds for SetOemReportUploadAsync and the OEM QA/QC report.
    // AddUploadAsync/the public POST endpoint don't enforce this, since they're the generic "add
    // a document" path — so callers that want the single-slot guarantee must go through those
    // two methods. EquipmentDetail.razor's admin UI blocks a user from creating a duplicate
    // photo or OEM report through the generic "Add document" form via IsReservedLabel, but
    // nothing at this layer (or the DB) stops another caller (import, seed script, another
    // endpoint) from inserting a second reserved-label row via AddUploadAsync if it doesn't
    // check first.
    public const string PhotoLabel = "Equipment Photo";
    private static readonly string[] PhotoLabels = [PhotoLabel, "Equipment Image"];

    // The OEM QA/QC report is the same shape of thing as the photo: exactly one per equipment,
    // stored as a Document row under a reserved label, replaced rather than appended by a
    // re-upload (SetOemReportUploadAsync). It gets its own admin section and its own panel on
    // the scan page, so it must be filtered out of the generic document list on both surfaces —
    // that is what IsReservedLabel is for.
    public const string OemReportLabel = "OEM QA/QC Report";

    public static bool IsPhotoLabel(string label) =>
        PhotoLabels.Any(p => p.Equals(label, StringComparison.OrdinalIgnoreCase));

    public static bool IsOemReportLabel(string label) =>
        OemReportLabel.Equals(label, StringComparison.OrdinalIgnoreCase);

    // Labels that own a dedicated slot in the UI and must never appear in the generic
    // "Documents" list beside manuals and safety data sheets.
    public static bool IsReservedLabel(string label) => IsPhotoLabel(label) || IsOemReportLabel(label);

    public static async Task<DocumentResult> AddUploadAsync(
        Guid equipmentId, string? label, byte[] content, string contentType, string fileName, AppDbContext db)
    {
        var validationError = DocumentUpload.Validate(fileName, contentType, content.LongLength, UploadKind.Document);
        if (validationError is not null)
        {
            return new DocumentResult.InvalidFile(validationError);
        }

        var equipmentExists = await db.Equipment.AnyAsync(e => e.Id == equipmentId);
        if (!equipmentExists)
        {
            return new DocumentResult.EquipmentNotFound();
        }

        var resolvedLabel = string.IsNullOrWhiteSpace(label) ? Path.GetFileNameWithoutExtension(fileName) : label;
        var document = new Document
        {
            Id = Guid.NewGuid(),
            EquipmentId = equipmentId,
            Label = resolvedLabel,
            Content = content,
            ContentType = contentType,
            FileName = fileName,
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return new DocumentResult.Success(document);
    }

    public static Task<DocumentResult> SetPhotoUploadAsync(
        Guid equipmentId, byte[] content, string contentType, string fileName, AppDbContext db) =>
        SetSingleDocumentAsync(equipmentId, content, contentType, fileName, UploadKind.Photo, PhotoLabel, IsPhotoLabel, db);

    public static Task<DocumentResult> SetOemReportUploadAsync(
        Guid equipmentId, byte[] content, string contentType, string fileName, AppDbContext db) =>
        SetSingleDocumentAsync(equipmentId, content, contentType, fileName, UploadKind.OemReport, OemReportLabel, IsOemReportLabel, db);

    // Shared by the photo and the OEM QA/QC report: both are one-per-equipment slots, so an
    // upload overwrites the existing row in place (keeping its Id, and therefore any URL already
    // printed or linked) instead of inserting a second row that would make "which one is it?"
    // depend on undefined DB row order.
    private static async Task<DocumentResult> SetSingleDocumentAsync(
        Guid equipmentId,
        byte[] content,
        string contentType,
        string fileName,
        UploadKind kind,
        string label,
        Func<string, bool> matchesLabel,
        AppDbContext db)
    {
        var validationError = DocumentUpload.Validate(fileName, contentType, content.LongLength, kind);
        if (validationError is not null)
        {
            return new DocumentResult.InvalidFile(validationError);
        }

        var equipmentExists = await db.Equipment.AnyAsync(e => e.Id == equipmentId);
        if (!equipmentExists)
        {
            return new DocumentResult.EquipmentNotFound();
        }

        var existingDocuments = await db.Documents.Where(d => d.EquipmentId == equipmentId).ToListAsync();
        var existing = existingDocuments.FirstOrDefault(d => matchesLabel(d.Label));

        if (existing is not null)
        {
            existing.Content = content;
            existing.ContentType = contentType;
            existing.FileName = fileName;
            existing.Url = null;
            await db.SaveChangesAsync();
            return new DocumentResult.Success(existing);
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            EquipmentId = equipmentId,
            Label = label,
            Content = content,
            ContentType = contentType,
            FileName = fileName,
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return new DocumentResult.Success(document);
    }

    public static async Task<DocumentResult> DeleteAsync(Guid id, AppDbContext db)
    {
        var document = await db.Documents.FindAsync(id);
        if (document is null)
        {
            return new DocumentResult.NotFound();
        }

        db.Documents.Remove(document);
        await db.SaveChangesAsync();
        return new DocumentResult.Success(document);
    }

    public static async Task<DocumentResult> GetContentAsync(Guid id, AppDbContext db)
    {
        var document = await db.Documents.FindAsync(id);
        if (document is null || document.Content is null)
        {
            return new DocumentResult.NotFound();
        }

        return new DocumentResult.Success(document);
    }

    public static Task<List<Document>> ListAsync(Guid equipmentId, AppDbContext db) =>
        db.Documents.Where(d => d.EquipmentId == equipmentId).ToListAsync();
}
