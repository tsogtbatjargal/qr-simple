using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api;

public abstract record EquipmentResult
{
    public sealed record Success(Equipment Equipment) : EquipmentResult;

    public sealed record NotFound : EquipmentResult;

    public sealed record UnknownCategory(string Category) : EquipmentResult;
}

public static class EquipmentCatalog
{
    public static async Task<EquipmentResult> CreateAsync(CreateEquipmentRequest request, AppDbContext db)
    {
        if (!await IsKnownCategoryAsync(request.Category, db))
        {
            return new EquipmentResult.UnknownCategory(request.Category);
        }

        var equipment = new Equipment
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Category = request.Category,
            SerialNumber = request.SerialNumber,
            Site = request.Site,
        };

        db.Equipment.Add(equipment);
        await db.SaveChangesAsync();

        return new EquipmentResult.Success(equipment);
    }

    public static async Task<EquipmentResult> UpdateAsync(Guid id, CreateEquipmentRequest request, AppDbContext db)
    {
        var equipment = await db.Equipment.FindAsync(id);
        if (equipment is null)
        {
            return new EquipmentResult.NotFound();
        }

        if (!await IsKnownCategoryAsync(request.Category, db))
        {
            return new EquipmentResult.UnknownCategory(request.Category);
        }

        equipment.Name = request.Name;
        equipment.Category = request.Category;
        equipment.SerialNumber = request.SerialNumber;
        equipment.Site = request.Site;
        await db.SaveChangesAsync();

        return new EquipmentResult.Success(equipment);
    }

    public static async Task<EquipmentResult> RetireAsync(Guid id, AppDbContext db) =>
        await SetStatusAsync(id, EquipmentStatus.Retired, db);

    public static async Task<EquipmentResult> ReactivateAsync(Guid id, AppDbContext db) =>
        await SetStatusAsync(id, EquipmentStatus.Active, db);

    private static async Task<EquipmentResult> SetStatusAsync(Guid id, EquipmentStatus status, AppDbContext db)
    {
        var equipment = await db.Equipment.FindAsync(id);
        if (equipment is null)
        {
            return new EquipmentResult.NotFound();
        }

        equipment.Status = status;
        await db.SaveChangesAsync();

        return new EquipmentResult.Success(equipment);
    }

    private static Task<bool> IsKnownCategoryAsync(string category, AppDbContext db) =>
        db.Categories.AnyAsync(c => c.Name == category);
}
