using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api;

public abstract record CategoryResult
{
    public sealed record Success(Category Category) : CategoryResult;
    public sealed record DuplicateName(string Name) : CategoryResult;

    public IResult ToHttpResult(Func<Category, IResult> onSuccess) => this switch
    {
        Success s => onSuccess(s.Category),
        DuplicateName d => Results.Conflict($"A category named \"{d.Name}\" already exists."),
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
}
