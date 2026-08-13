using System.Security.Claims;

namespace QrSimple.Api;

public sealed class RequireRoleFilter(params string[] allowedRoles) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var email = context.HttpContext.User.FindFirstValue(ClaimTypes.Email);
        var user = await UserAuthorization.FindAsync(email, db);

        if (user is null || !allowedRoles.Contains(user.Role))
        {
            return Results.Json(
                "You are not authorized to perform this action.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return await next(context);
    }
}
