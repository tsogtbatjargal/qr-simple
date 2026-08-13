using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api;

public static class UserAuthorization
{
    public static Task<User?> FindAsync(string? email, AppDbContext db) =>
        email is null ? Task.FromResult<User?>(null) : db.Users.SingleOrDefaultAsync(u => u.Email == email);
}
