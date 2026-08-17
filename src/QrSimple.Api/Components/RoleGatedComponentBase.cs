using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api.Components;

public abstract class RoleGatedComponentBase : ComponentBase
{
    [Inject] protected IDbContextFactory<AppDbContext> DbFactory { get; set; } = default!;
    [Inject] protected NavigationManager Navigation { get; set; } = default!;
    [Inject] protected ToastService Toast { get; set; } = default!;
    [CascadingParameter] protected Task<AuthenticationState>? AuthenticationStateTask { get; set; }

    protected abstract string[] AllowedRoles { get; }

    protected override async Task OnInitializedAsync()
    {
        var authState = await AuthenticationStateTask!;
        var email = authState.User.FindFirst(ClaimTypes.Email)?.Value;

        await using var db = await DbFactory.CreateDbContextAsync();
        var user = await UserAuthorization.FindAsync(email, db);

        if (user is null || !user.IsActive || !AllowedRoles.Contains(user.Role))
        {
            Navigation.NavigateTo("/app/not-authorized");
            return;
        }

        await OnAuthorizedInitializedAsync(db);
    }

    protected virtual Task OnAuthorizedInitializedAsync(AppDbContext db) => Task.CompletedTask;
}
