using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api;

public abstract record DocumentResult
{
    public sealed record Success(Document Document) : DocumentResult;
    public sealed record NotFound : DocumentResult;
    public sealed record EquipmentNotFound : DocumentResult;

    public IResult ToHttpResult(Func<Document, IResult> onSuccess) => this switch
    {
        Success s => onSuccess(s.Document),
        NotFound => Results.NotFound(),
        EquipmentNotFound => Results.NotFound(),
        _ => Results.Problem(),
    };
}

public static class DocumentCatalog
{
    // ScanPage.cs picks the header photo with FirstOrDefault against these labels, so if more
    // than one existed for the same equipment the "winner" would depend on undefined DB row
    // order. SetPhotoAsync is the only path that keeps that invariant (one photo per equipment)
    // by overwriting the existing photo row instead of inserting a second one. AddAsync/the
    // public POST endpoint don't enforce this, since they're the generic "add a document link"
    // path and existing API tests already add a document literally labeled "Equipment Photo"
    // through it — so callers that want the single-photo guarantee must go through SetPhotoAsync.
    public const string PhotoLabel = "Equipment Photo";
    private static readonly string[] PhotoLabels = [PhotoLabel, "Equipment Image"];

    public static bool IsPhotoLabel(string label) =>
        PhotoLabels.Any(p => p.Equals(label, StringComparison.OrdinalIgnoreCase));

    public static async Task<DocumentResult> AddAsync(Guid equipmentId, string label, string url, AppDbContext db)
    {
        var equipmentExists = await db.Equipment.AnyAsync(e => e.Id == equipmentId);
        if (!equipmentExists)
        {
            return new DocumentResult.EquipmentNotFound();
        }

        var document = new Document
        {
            Id = Guid.NewGuid(),
            EquipmentId = equipmentId,
            Label = label,
            Url = url,
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync();
        return new DocumentResult.Success(document);
    }

    public static async Task<DocumentResult> SetPhotoAsync(Guid equipmentId, string url, AppDbContext db)
    {
        var equipmentExists = await db.Equipment.AnyAsync(e => e.Id == equipmentId);
        if (!equipmentExists)
        {
            return new DocumentResult.EquipmentNotFound();
        }

        var existingPhoto = await db.Documents
            .Where(d => d.EquipmentId == equipmentId)
            .ToListAsync();
        var photo = existingPhoto.FirstOrDefault(d => IsPhotoLabel(d.Label));

        if (photo is not null)
        {
            photo.Url = url;
            await db.SaveChangesAsync();
            return new DocumentResult.Success(photo);
        }

        return await AddAsync(equipmentId, PhotoLabel, url, db);
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

    public static Task<List<Document>> ListAsync(Guid equipmentId, AppDbContext db) =>
        db.Documents.Where(d => d.EquipmentId == equipmentId).ToListAsync();
}
