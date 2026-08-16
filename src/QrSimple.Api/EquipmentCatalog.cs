using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api;

public abstract record EquipmentResult
{
    public sealed record Success(Equipment Equipment) : EquipmentResult;

    public sealed record NotFound : EquipmentResult;

    public sealed record UnknownCategory(string Category) : EquipmentResult;

    public sealed record RestrictedFieldEdit : EquipmentResult;

    // Success shape (Created vs Ok) varies per endpoint; NotFound/UnknownCategory don't, so they're mapped once here.
    public IResult ToHttpResult(Func<Equipment, IResult> onSuccess) => this switch
    {
        Success s => onSuccess(s.Equipment),
        NotFound => Results.NotFound(),
        UnknownCategory u => Results.BadRequest($"Unknown category: {u.Category}"),
        RestrictedFieldEdit => Results.Json(
            "Operators can only edit Category and Site. Ask an admin to change Name or Serial Number.",
            statusCode: StatusCodes.Status403Forbidden),
        _ => Results.Problem(),
    };
}

public static class EquipmentCatalog
{
    public static async Task<EquipmentResult> CreateAsync(CreateEquipmentRequest request, AppDbContext db)
    {
        if (!await IsKnownCategoryAsync(request.Category, db))
        {
            return new EquipmentResult.UnknownCategory(request.Category);
        }

        var equipment = Equipment.Create(request.Name, request.Category, request.SerialNumber, request.Site);

        db.Equipment.Add(equipment);
        await db.SaveChangesAsync();

        return new EquipmentResult.Success(equipment);
    }

    public static async Task<EquipmentResult> UpdateAsync(Guid id, CreateEquipmentRequest request, string callerRole, AppDbContext db)
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

        if (callerRole == Roles.Operator &&
            (request.Name != equipment.Name || request.SerialNumber != equipment.SerialNumber))
        {
            return new EquipmentResult.RestrictedFieldEdit();
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

    public static Task<List<Equipment>> ListAsync(bool includeRetired, AppDbContext db)
    {
        var query = db.Equipment.AsQueryable();
        if (!includeRetired)
        {
            query = query.Where(e => e.Status == EquipmentStatus.Active);
        }

        return query.ToListAsync();
    }

    private static Task<bool> IsKnownCategoryAsync(string category, AppDbContext db) =>
        db.Categories.AnyAsync(c => c.Name == category);
}
