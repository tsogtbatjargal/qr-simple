using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api;

public abstract record InspectionResult
{
    public sealed record Success(Inspection Inspection) : InspectionResult;
    public sealed record NotFound : InspectionResult;
    public sealed record EquipmentNotFound : InspectionResult;
    public sealed record InvalidFile(string Reason) : InspectionResult;
    public sealed record InvalidRequest(string Reason) : InspectionResult;
    public sealed record Forbidden(string Reason) : InspectionResult;

    public IResult ToHttpResult(Func<Inspection, IResult> onSuccess) => this switch
    {
        Success s => onSuccess(s.Inspection),
        NotFound => Results.NotFound(),
        EquipmentNotFound => Results.NotFound(),
        InvalidFile invalid => Results.BadRequest(new { error = invalid.Reason }),
        InvalidRequest invalid => Results.BadRequest(new { error = invalid.Reason }),
        Forbidden forbidden => Results.Json(new { error = forbidden.Reason }, statusCode: StatusCodes.Status403Forbidden),
        _ => Results.Problem(),
    };
}

// Content deliberately omitted — this is the projection ListAsync must use so a page listing
// 50 inspections doesn't pull 50 PDFs into memory to render a page that shows none of the
// bytes. GetContentAsync is the only path that loads Content.
public sealed record InspectionListItem(
    Guid Id,
    Guid EquipmentId,
    string Kind,
    DateOnly InspectionDate,
    string? Note,
    string ContentType,
    string FileName,
    string UploadedByEmail,
    DateTimeOffset UploadedAtUtc,
    DateTimeOffset? LastEditedAtUtc,
    string? LastEditedByEmail);

public static class InspectionCatalog
{
    public const int MaxNoteLength = 1000;

    public static async Task<InspectionResult> AddAsync(
        Guid equipmentId,
        string kind,
        DateOnly inspectionDate,
        string? note,
        byte[] content,
        string contentType,
        string fileName,
        string uploadedByEmail,
        AppDbContext db)
    {
        var validationError = DocumentUpload.Validate(fileName, contentType, content.LongLength, UploadKind.Inspection);
        if (validationError is not null)
        {
            return new InspectionResult.InvalidFile(validationError);
        }

        if (!InspectionKinds.IsKnown(kind))
        {
            return new InspectionResult.InvalidRequest($"Unknown inspection kind: {kind}");
        }

        if (inspectionDate > BusinessTime.Today())
        {
            return new InspectionResult.InvalidRequest("Inspection date cannot be in the future.");
        }

        if (note is { Length: > MaxNoteLength })
        {
            return new InspectionResult.InvalidRequest($"Note cannot exceed {MaxNoteLength} characters.");
        }

        var equipment = await db.Equipment.FindAsync(equipmentId);
        if (equipment is null)
        {
            return new InspectionResult.EquipmentNotFound();
        }

        // Retired equipment keeps its inspection history readable but can't grow one further —
        // you don't inspect a decommissioned machine, and allowing it silently corrupts the
        // record. See docs/plans/0002-inspection-records.md decision 15; also enforced in the
        // admin UI, but that alone wouldn't stop a direct call here.
        if (equipment.Status == EquipmentStatus.Retired)
        {
            return new InspectionResult.Forbidden("This equipment is retired; new inspection records cannot be added.");
        }

        var inspection = new Inspection
        {
            Id = Guid.NewGuid(),
            EquipmentId = equipmentId,
            Kind = kind,
            InspectionDate = inspectionDate,
            Note = string.IsNullOrWhiteSpace(note) ? null : note,
            Content = content,
            ContentType = contentType,
            FileName = fileName,
            UploadedByEmail = uploadedByEmail,
            UploadedAtUtc = DateTimeOffset.UtcNow,
        };

        db.Inspections.Add(inspection);
        await db.SaveChangesAsync();
        return new InspectionResult.Success(inspection);
    }

    // Operators may correct Note/InspectionDate on records they uploaded; the PDF itself is
    // immutable (replacing it changes what the record attests to — that's delete-and-re-upload,
    // which is Admin-gated). Admins may edit any record. See decision 13.
    public static async Task<InspectionResult> EditAsync(
        Guid inspectionId, DateOnly inspectionDate, string? note, string callerEmail, string callerRole, AppDbContext db)
    {
        var inspection = await db.Inspections.FindAsync(inspectionId);
        if (inspection is null)
        {
            return new InspectionResult.NotFound();
        }

        if (callerRole != Roles.Admin && !string.Equals(inspection.UploadedByEmail, callerEmail, StringComparison.OrdinalIgnoreCase))
        {
            return new InspectionResult.Forbidden("Only the uploader or an Admin can edit this record.");
        }

        if (inspectionDate > BusinessTime.Today())
        {
            return new InspectionResult.InvalidRequest("Inspection date cannot be in the future.");
        }

        if (note is { Length: > MaxNoteLength })
        {
            return new InspectionResult.InvalidRequest($"Note cannot exceed {MaxNoteLength} characters.");
        }

        inspection.InspectionDate = inspectionDate;
        inspection.Note = string.IsNullOrWhiteSpace(note) ? null : note;
        inspection.LastEditedAtUtc = DateTimeOffset.UtcNow;
        inspection.LastEditedByEmail = callerEmail;

        await db.SaveChangesAsync();
        return new InspectionResult.Success(inspection);
    }

    public static Task<List<InspectionListItem>> ListAsync(Guid equipmentId, AppDbContext db) =>
        db.Inspections
            .Where(i => i.EquipmentId == equipmentId)
            .OrderByDescending(i => i.InspectionDate)
            .ThenByDescending(i => i.UploadedAtUtc)
            .Select(i => new InspectionListItem(
                i.Id,
                i.EquipmentId,
                i.Kind,
                i.InspectionDate,
                i.Note,
                i.ContentType,
                i.FileName,
                i.UploadedByEmail,
                i.UploadedAtUtc,
                i.LastEditedAtUtc,
                i.LastEditedByEmail))
            .ToListAsync();

    public static Task<int> CountAsync(Guid equipmentId, AppDbContext db) =>
        db.Inspections.CountAsync(i => i.EquipmentId == equipmentId);

    public static async Task<InspectionResult> GetContentAsync(Guid id, AppDbContext db)
    {
        var inspection = await db.Inspections.FindAsync(id);
        if (inspection is null)
        {
            return new InspectionResult.NotFound();
        }

        return new InspectionResult.Success(inspection);
    }

    public static async Task<InspectionResult> DeleteAsync(Guid id, AppDbContext db)
    {
        var inspection = await db.Inspections.FindAsync(id);
        if (inspection is null)
        {
            return new InspectionResult.NotFound();
        }

        db.Inspections.Remove(inspection);
        await db.SaveChangesAsync();
        return new InspectionResult.Success(inspection);
    }

    // Pure, no DB — decisions 16-17 are unit-testable without a container. `ordered` must
    // already be sorted newest-first (ListAsync's order); an item exactly `months` back counts
    // as recent (inclusive). The recent set is never smaller than `minimumRecent`: for
    // Annual/Quarterly regimes the last `months` can legitimately contain zero records while
    // several reports sit hidden under "Older," which would otherwise read as an empty page.
    public static (IReadOnlyList<T> Recent, IReadOnlyList<T> Older) SplitByRecency<T>(
        IReadOnlyList<T> ordered, DateOnly today, Func<T, DateOnly> dateOf, int months = 6, int minimumRecent = 3)
    {
        var cutoff = today.AddMonths(-months);
        var recent = new List<T>();
        var older = new List<T>();

        foreach (var item in ordered)
        {
            if (dateOf(item) >= cutoff)
            {
                recent.Add(item);
            }
            else
            {
                older.Add(item);
            }
        }

        if (recent.Count < minimumRecent && older.Count > 0)
        {
            var needed = Math.Min(minimumRecent - recent.Count, older.Count);
            recent.AddRange(older.Take(needed));
            older = older.Skip(needed).ToList();
        }

        return (recent, older);
    }
}
