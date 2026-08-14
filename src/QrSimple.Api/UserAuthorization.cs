using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api;

public static class UserAuthorization
{
    public static Task<User?> FindAsync(string? email, AppDbContext db) =>
        email is null ? Task.FromResult<User?>(null) : db.Users.SingleOrDefaultAsync(u => u.Email == email);

    public static IResult? RequireRole(User? caller, params string[] allowedRoles) =>
        caller is null || !allowedRoles.Contains(caller.Role)
            ? Results.Json(
                "You are not authorized to perform this action.",
                statusCode: StatusCodes.Status403Forbidden)
            : null;
}
