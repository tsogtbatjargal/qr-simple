using System.Security.Claims;

namespace QrSimple.Api;

public sealed class RequireRoleFilter(params string[] allowedRoles) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var email = context.HttpContext.User.FindFirstValue(ClaimTypes.Email);
        var user = await UserAuthorization.FindAsync(email, db);

        var denial = UserAuthorization.RequireRole(user, allowedRoles);
        return denial ?? await next(context);
    }
}
