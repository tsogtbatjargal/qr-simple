using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api;

public abstract record CategoryResult
{
    public sealed record Success(Category Category) : CategoryResult;
    public sealed record DuplicateName(string Name) : CategoryResult;
    public sealed record NotFound : CategoryResult;
    public sealed record InUse(int EquipmentCount) : CategoryResult;

    public IResult ToHttpResult(Func<Category, IResult> onSuccess) => this switch
    {
        Success s => onSuccess(s.Category),
        DuplicateName d => Results.Conflict($"A category named \"{d.Name}\" already exists."),
        NotFound => Results.NotFound(),
        InUse i => Results.Conflict($"{i.EquipmentCount} equipment record(s) still use this category."),
        _ => Results.Problem(),
    };
}

public static class CategoryCatalog
{
    public static async Task<CategoryResult> CreateAsync(string name, AppDbContext db)
    {
        if (await db.Categories.AnyAsync(c => c.Name == name))
        {
            return new CategoryResult.DuplicateName(name);
        }

        var category = new Category { Id = Guid.NewGuid(), Name = name };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return new CategoryResult.Success(category);
    }

    // Renaming cascades to every Equipment row carrying the old name (Equipment.Category is a
    // free-text copy, not a foreign key) so existing equipment doesn't silently point at a
    // category name that no longer exists. Both writes happen in one transaction so a category
    // never ends up renamed without its equipment following, or vice versa.
    public static async Task<CategoryResult> RenameAsync(Guid id, string newName, AppDbContext db)
    {
        var category = await db.Categories.FindAsync(id);
        if (category is null)
        {
            return new CategoryResult.NotFound();
        }

        if (newName != category.Name && await db.Categories.AnyAsync(c => c.Name == newName))
        {
            return new CategoryResult.DuplicateName(newName);
        }

        if (newName != category.Name)
        {
            var oldName = category.Name;
            await using var transaction = await db.Database.BeginTransactionAsync();

            await db.Equipment
                .Where(e => e.Category == oldName)
                .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.Category, newName));

            category.Name = newName;
            await db.SaveChangesAsync();

            await transaction.CommitAsync();
        }

        return new CategoryResult.Success(category);
    }

    // Deleting is intentionally blocked while any equipment still references the category —
    // unlike rename, there's no sensible name to cascade equipment onto, and silently clearing
    // Equipment.Category would leave those records without a valid category in the dropdown.
    // Callers must reassign or retire the equipment first.
    public static async Task<CategoryResult> DeleteAsync(Guid id, AppDbContext db)
    {
        var category = await db.Categories.FindAsync(id);
        if (category is null)
        {
            return new CategoryResult.NotFound();
        }

        var equipmentCount = await db.Equipment.CountAsync(e => e.Category == category.Name);
        if (equipmentCount > 0)
        {
            return new CategoryResult.InUse(equipmentCount);
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        return new CategoryResult.Success(category);
    }
}
