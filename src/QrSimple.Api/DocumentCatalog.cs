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

public static class DocumentUpload
{
    public const long MaxPhotoBytes = 5 * 1024 * 1024;
    public const long MaxDocumentBytes = 20 * 1024 * 1024;

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

    // Extension + browser-reported ContentType only, no magic-byte sniffing — internal tool,
    // trusted Admin/Operator roles (see docs/plans/0001-document-file-upload.md decision #4).
    public static string? Validate(string fileName, string contentType, long sizeBytes, bool isPhoto)
    {
        var extensions = isPhoto ? PhotoExtensions : DocumentExtensions;
        var contentTypes = isPhoto ? PhotoContentTypes : DocumentContentTypes;
        var maxBytes = isPhoto ? MaxPhotoBytes : MaxDocumentBytes;
        var kind = isPhoto ? "photo" : "document";

        if (sizeBytes <= 0)
        {
            return "File is empty.";
        }

        if (sizeBytes > maxBytes)
        {
            return $"File is too large. Maximum size for a {kind} is {maxBytes / (1024 * 1024)}MB.";
        }

        var extension = Path.GetExtension(fileName);
        if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
            !contentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return $"Unsupported file type for a {kind}. Allowed: {string.Join(", ", extensions)}.";
        }

        return null;
    }
}

public static class DocumentCatalog
{
    // ScanPage.cs picks the header photo with FirstOrDefault against these labels, so if more
    // than one existed for the same equipment the "winner" would depend on undefined DB row
    // order. SetPhotoUploadAsync is the only path that keeps that invariant (one photo per
    // equipment) by overwriting the existing photo row instead of inserting a second one.
    // AddUploadAsync/the public POST endpoint don't enforce this, since they're the generic "add
    // a document" path — so callers that want the single-photo guarantee must go through
    // SetPhotoUploadAsync. EquipmentDetail.razor's admin UI blocks a user from creating a
    // duplicate photo through the generic "Add document" form via IsPhotoLabel, but nothing at
    // this layer (or the DB) stops another caller (import, seed script, another endpoint) from
    // inserting a second "Equipment Photo"-labeled row via AddUploadAsync if it doesn't check first.
    public const string PhotoLabel = "Equipment Photo";
    private static readonly string[] PhotoLabels = [PhotoLabel, "Equipment Image"];

    public static bool IsPhotoLabel(string label) =>
        PhotoLabels.Any(p => p.Equals(label, StringComparison.OrdinalIgnoreCase));

    public static async Task<DocumentResult> AddUploadAsync(
        Guid equipmentId, string? label, byte[] content, string contentType, string fileName, AppDbContext db)
    {
        var validationError = DocumentUpload.Validate(fileName, contentType, content.LongLength, isPhoto: false);
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

    public static async Task<DocumentResult> SetPhotoUploadAsync(
        Guid equipmentId, byte[] content, string contentType, string fileName, AppDbContext db)
    {
        var validationError = DocumentUpload.Validate(fileName, contentType, content.LongLength, isPhoto: true);
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
        var photo = existingDocuments.FirstOrDefault(d => IsPhotoLabel(d.Label));

        if (photo is not null)
        {
            photo.Content = content;
            photo.ContentType = contentType;
            photo.FileName = fileName;
            photo.Url = null;
            await db.SaveChangesAsync();
            return new DocumentResult.Success(photo);
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            EquipmentId = equipmentId,
            Label = PhotoLabel,
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
