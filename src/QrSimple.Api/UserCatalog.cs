using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api;

public abstract record UserResult
{
    public sealed record Success(User User) : UserResult;

    public sealed record NotFound : UserResult;

    public sealed record UnknownRole(string Role) : UserResult;

    public sealed record DuplicateEmail(string Email) : UserResult;

    public sealed record LastAdminProtected : UserResult;

    public sealed record SelfLockoutBlocked : UserResult;

    public IResult ToHttpResult(Func<User, IResult> onSuccess) => this switch
    {
        Success s => onSuccess(s.User),
        NotFound => Results.NotFound(),
        UnknownRole u => Results.BadRequest($"Unknown role: {u.Role}"),
        DuplicateEmail d => Results.Conflict($"A user with email {d.Email} already exists."),
        LastAdminProtected => Results.BadRequest("Cannot remove the last remaining Admin."),
        SelfLockoutBlocked => Results.BadRequest("You cannot deactivate your own account."),
        _ => Results.Problem(),
    };
}

public static class UserCatalog
{
    public static async Task<UserResult> CreateAsync(string email, string role, AppDbContext db)
    {
        if (!Roles.IsKnown(role))
        {
            return new UserResult.UnknownRole(role);
        }

        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            return new UserResult.DuplicateEmail(email);
        }

        var user = new User { Id = Guid.NewGuid(), Email = email, Role = role };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return new UserResult.Success(user);
    }

    public static async Task<UserResult> UpdateRoleAsync(Guid id, string newRole, AppDbContext db)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
        {
            return new UserResult.NotFound();
        }

        if (!Roles.IsKnown(newRole))
        {
            return new UserResult.UnknownRole(newRole);
        }

        if (user.Role == Roles.Admin && newRole != Roles.Admin && await IsLastActiveAdminAsync(user, db))
        {
            return new UserResult.LastAdminProtected();
        }

        user.Role = newRole;
        await db.SaveChangesAsync();

        return new UserResult.Success(user);
    }

    public static Task<UserResult> DeactivateAsync(Guid id, string? callerEmail, AppDbContext db) =>
        SetActiveAsync(id, isActive: false, callerEmail, db);

    public static Task<UserResult> ReactivateAsync(Guid id, AppDbContext db) =>
        SetActiveAsync(id, isActive: true, callerEmail: null, db);

    private static async Task<UserResult> SetActiveAsync(Guid id, bool isActive, string? callerEmail, AppDbContext db)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null)
        {
            return new UserResult.NotFound();
        }

        if (!isActive)
        {
            if (callerEmail is not null && user.Email == callerEmail)
            {
                return new UserResult.SelfLockoutBlocked();
            }

            if (user.Role == Roles.Admin && user.IsActive && await IsLastActiveAdminAsync(user, db))
            {
                return new UserResult.LastAdminProtected();
            }
        }

        user.IsActive = isActive;
        await db.SaveChangesAsync();

        return new UserResult.Success(user);
    }

    private static async Task<bool> IsLastActiveAdminAsync(User user, AppDbContext db) =>
        await db.Users.CountAsync(u => u.Role == Roles.Admin && u.IsActive && u.Id != user.Id) == 0;
}
