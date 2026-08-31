using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api;

public abstract record RebuildResult
{
    public sealed record Success(Rebuild Rebuild) : RebuildResult;
    public sealed record NotFound : RebuildResult;
    public sealed record EquipmentNotFound : RebuildResult;
    public sealed record InvalidFile(string Reason) : RebuildResult;
    public sealed record InvalidRequest(string Reason) : RebuildResult;
    public sealed record Forbidden(string Reason) : RebuildResult;

    public IResult ToHttpResult(Func<Rebuild, IResult> onSuccess) => this switch
    {
        Success s => onSuccess(s.Rebuild),
        NotFound => Results.NotFound(),
        EquipmentNotFound => Results.NotFound(),
        InvalidFile invalid => Results.BadRequest(new { error = invalid.Reason }),
        InvalidRequest invalid => Results.BadRequest(new { error = invalid.Reason }),
        Forbidden forbidden => Results.Json(new { error = forbidden.Reason }, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.Problem(),
    };
}

// Content deliberately omitted — this is the projection ListAsync must use so a page listing
// many rebuilds doesn't pull every PDF into memory to render a page that shows none of the
// bytes. GetContentAsync is the only path that loads Content. HasFile stands in for it, so
// callers can render the "Open PDF" link without touching the blob.
public sealed record RebuildListItem(
    Guid Id,
    Guid EquipmentId,
    DateOnly RebuildDate,
    string Note,
    bool HasFile,
    string? ContentType,
    string? FileName,
    string UploadedByEmail,
    DateTimeOffset UploadedAtUtc,
    DateTimeOffset? LastEditedAtUtc,
    string? LastEditedByEmail);

public static class RebuildCatalog
{
    public const int MaxNoteLength = 1000;

    public static async Task<RebuildResult> AddAsync(
        Guid equipmentId,
        DateOnly rebuildDate,
        string? note,
        byte[]? content,
        string? contentType,
        string? fileName,
        string uploadedByEmail,
        AppDbContext db)
    {
        // The PDF is optional, so validate it only when one was actually supplied. Note and
        // date carry the record on their own.
        if (content is not null)
        {
            var validationError = DocumentUpload.Validate(fileName ?? "", contentType ?? "", content.LongLength, UploadKind.Rebuild);
            if (validationError is not null)
            {
                return new RebuildResult.InvalidFile(validationError);
            }
        }

        var noteError = ValidateNote(note);
        if (noteError is not null)
        {
            return noteError;
        }

        if (rebuildDate > BusinessTime.Today())
        {
            return new RebuildResult.InvalidRequest("Rebuild date cannot be in the future.");
        }

        var equipment = await db.Equipment.FindAsync(equipmentId);
        if (equipment is null)
        {
            return new RebuildResult.EquipmentNotFound();
        }

        // Retired equipment keeps its rebuild history readable but can't grow one further —
        // you don't rebuild a decommissioned machine, and allowing it silently corrupts the
        // record. See docs/plans/0002-inspection-records.md decision 15; also enforced in the
        // admin UI, but that alone wouldn't stop a direct call here.
        if (equipment.Status == EquipmentStatus.Retired)
        {
            return new RebuildResult.Forbidden("This equipment is retired; new rebuild records cannot be added.");
        }

        var rebuild = new Rebuild
        {
            Id = Guid.NewGuid(),
            EquipmentId = equipmentId,
            RebuildDate = rebuildDate,
            Note = note!.Trim(),
            Content = content,
            ContentType = content is null ? null : contentType,
            FileName = content is null ? null : fileName,
            UploadedByEmail = uploadedByEmail,
            UploadedAtUtc = DateTimeOffset.UtcNow,
        };

        db.Rebuilds.Add(rebuild);
        await db.SaveChangesAsync();
        return new RebuildResult.Success(rebuild);
    }

    // Operators may correct Note/RebuildDate on records they uploaded; an attached PDF is
    // immutable (replacing it changes what the record attests to — that's delete-and-re-upload,
    // which is Admin-gated). Admins may edit any record. See decision 13.
    public static async Task<RebuildResult> EditAsync(
        Guid rebuildId, DateOnly rebuildDate, string? note, string callerEmail, string callerRole, AppDbContext db)
    {
        var rebuild = await db.Rebuilds.FindAsync(rebuildId);
        if (rebuild is null)
        {
            return new RebuildResult.NotFound();
        }

        var forbidden = CheckEditPermission(rebuild, callerEmail, callerRole);
        if (forbidden is not null)
        {
            return forbidden;
        }

        if (rebuildDate > BusinessTime.Today())
        {
            return new RebuildResult.InvalidRequest("Rebuild date cannot be in the future.");
        }

        var noteError = ValidateNote(note);
        if (noteError is not null)
        {
            return noteError;
        }

        rebuild.RebuildDate = rebuildDate;
        rebuild.Note = note!.Trim();
        rebuild.LastEditedAtUtc = DateTimeOffset.UtcNow;
        rebuild.LastEditedByEmail = callerEmail;

        await db.SaveChangesAsync();
        return new RebuildResult.Success(rebuild);
    }

    // Attach-only, never replace. A record filed without a PDF (the point of making it
    // optional) can gain one later; a record that already has one keeps it, because swapping
    // the evidence under an unchanged note/date is exactly the ambiguity decision 13 exists to
    // prevent. To swap it, an Admin deletes the record and files it again.
    public static async Task<RebuildResult> AttachFileAsync(
        Guid rebuildId, byte[] content, string contentType, string fileName, string callerEmail, string callerRole, AppDbContext db)
    {
        var rebuild = await db.Rebuilds.FindAsync(rebuildId);
        if (rebuild is null)
        {
            return new RebuildResult.NotFound();
        }

        var forbidden = CheckEditPermission(rebuild, callerEmail, callerRole);
        if (forbidden is not null)
        {
            return forbidden;
        }

        if (rebuild.Content is not null)
        {
            return new RebuildResult.Forbidden(
                "This rebuild record already has a PDF. Delete the record and file it again to replace the file.");
        }

        var validationError = DocumentUpload.Validate(fileName, contentType, content.LongLength, UploadKind.Rebuild);
        if (validationError is not null)
        {
            return new RebuildResult.InvalidFile(validationError);
        }

        rebuild.Content = content;
        rebuild.ContentType = contentType;
        rebuild.FileName = fileName;
        rebuild.LastEditedAtUtc = DateTimeOffset.UtcNow;
        rebuild.LastEditedByEmail = callerEmail;

        await db.SaveChangesAsync();
        return new RebuildResult.Success(rebuild);
    }

    private static RebuildResult? CheckEditPermission(Rebuild rebuild, string callerEmail, string callerRole) =>
        callerRole != Roles.Admin && !string.Equals(rebuild.UploadedByEmail, callerEmail, StringComparison.OrdinalIgnoreCase)
            ? new RebuildResult.Forbidden("Only the uploader or an Admin can edit this record.")
            : null;

    private static RebuildResult? ValidateNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return new RebuildResult.InvalidRequest("Note is required.");
        }

        return note.Trim().Length > MaxNoteLength
            ? new RebuildResult.InvalidRequest($"Note cannot exceed {MaxNoteLength} characters.")
            : null;
    }

    public static Task<List<RebuildListItem>> ListAsync(Guid equipmentId, AppDbContext db) =>
        db.Rebuilds
            .Where(r => r.EquipmentId == equipmentId)
            .OrderByDescending(r => r.RebuildDate)
            .ThenByDescending(r => r.UploadedAtUtc)
            .Select(r => new RebuildListItem(
                r.Id,
                r.EquipmentId,
                r.RebuildDate,
                r.Note,
                r.Content != null,
                r.ContentType,
                r.FileName,
                r.UploadedByEmail,
                r.UploadedAtUtc,
                r.LastEditedAtUtc,
                r.LastEditedByEmail))
            .ToListAsync();

    public static Task<int> CountAsync(Guid equipmentId, AppDbContext db) =>
        db.Rebuilds.CountAsync(r => r.EquipmentId == equipmentId);

    public static async Task<RebuildResult> GetContentAsync(Guid id, AppDbContext db)
    {
        var rebuild = await db.Rebuilds.FindAsync(id);
        if (rebuild is null || rebuild.Content is null)
        {
            return new RebuildResult.NotFound();
        }

        return new RebuildResult.Success(rebuild);
    }

    public static async Task<RebuildResult> DeleteAsync(Guid id, AppDbContext db)
    {
        var rebuild = await db.Rebuilds.FindAsync(id);
        if (rebuild is null)
        {
            return new RebuildResult.NotFound();
        }

        db.Rebuilds.Remove(rebuild);
        await db.SaveChangesAsync();
        return new RebuildResult.Success(rebuild);
    }
}
