using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using QrSimple.Api;
using QrSimple.Api.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddOpenApi();
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
    })
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? "";
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "";
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Before this first runs against a database created via the old EnsureCreated() path (no
// __EFMigrationsHistory table yet), see docs/database-migrations.md for a required one-time
// step — otherwise Migrate() tries to CREATE TABLE for tables that already exist and crashes.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

// Fly.io (and most PaaS platforms) terminate TLS at the edge and forward plain
// HTTP to the container; without this, UseHttpsRedirection() below would see
// every request as HTTP and redirect-loop, and Google's OAuth challenge would
// build an http:// redirect_uri that never matches the https:// one registered
// in Google Cloud Console. KnownNetworks/KnownProxies are cleared because the
// edge proxy's address isn't a fixed IP we can allowlist -- standard pattern
// for containerized deploys behind a platform-managed proxy. No-op locally
// (nothing sets these headers in dev, so the app behaves exactly as before).
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// MapStaticAssets (not UseStaticFiles) is required for framework assets like
// _framework/blazor.web.js: the SDK's newer static-web-assets publish model
// emits an endpoints manifest instead of physically copying files into
// wwwroot, and only MapStaticAssets knows how to serve from that manifest.
// UseStaticFiles alone 404s on every framework asset in a published build,
// even though `dotnet run` from source still works (it doesn't go through
// the same publish-time manifest).
app.MapStaticAssets();

app.MapGet("/login", (string? returnUrl) =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = returnUrl ?? "/app" },
        [GoogleDefaults.AuthenticationScheme]));

app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/app");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/equipment", async (CreateEquipmentRequest request, AppDbContext db) =>
{
    var result = await EquipmentCatalog.CreateAsync(request, db);
    return result.ToHttpResult(equipment => Results.Created($"/equipment/{equipment.Id}", equipment));
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter(Roles.Admin, Roles.Operator));

app.MapGet("/equipment", async (bool? includeRetired, AppDbContext db) =>
    Results.Ok(await EquipmentCatalog.ListAsync(includeRetired == true, db)))
    .RequireAuthorization().AddEndpointFilter(new RequireRoleFilter(Roles.Admin, Roles.Operator, Roles.Reader));

app.MapGet("/equipment/{id}/qr", async (Guid id, AppDbContext db, IConfiguration config) =>
{
    var equipment = await db.Equipment.FindAsync(id);
    if (equipment is null)
    {
        return Results.NotFound();
    }

    var url = $"{config["PublicBaseUrl"]}/e/{equipment.Id}";
    var qrCode = QrCode.GeneratePng(url);

    return Results.File(qrCode, "image/png");
});

app.MapGet("/e/{id}", async (Guid id, AppDbContext db) =>
{
    var equipment = await db.Equipment.FindAsync(id);
    if (equipment is null)
    {
        return Results.NotFound();
    }

    var documents = await db.Documents.Where(d => d.EquipmentId == id).ToListAsync();

    return Results.Content(ScanPage.Render(equipment, documents), "text/html");
});

app.MapPost("/equipment/{id}/documents", async (Guid id, AddDocumentRequest request, AppDbContext db) =>
{
    var equipment = await db.Equipment.FindAsync(id);
    if (equipment is null)
    {
        return Results.NotFound();
    }

    var document = new Document
    {
        Id = Guid.NewGuid(),
        EquipmentId = id,
        Label = request.Label,
        Url = request.Url,
    };

    db.Documents.Add(document);
    await db.SaveChangesAsync();

    return Results.Created($"/equipment/{id}/documents/{document.Id}", document);
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter(Roles.Admin, Roles.Operator));

app.MapPut("/equipment/{id}", async (Guid id, CreateEquipmentRequest request, ClaimsPrincipal principal, AppDbContext db) =>
{
    var caller = await UserAuthorization.FindAsync(principal.FindFirstValue(ClaimTypes.Email), db);
    var result = await EquipmentCatalog.UpdateAsync(id, request, caller!.Role, db);
    return result.ToHttpResult(Results.Ok);
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter(Roles.Admin, Roles.Operator));

app.MapPost("/equipment/{id}/retire", async (Guid id, AppDbContext db) =>
{
    var result = await EquipmentCatalog.RetireAsync(id, db);
    return result.ToHttpResult(Results.Ok);
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter(Roles.Admin));

app.MapPost("/equipment/{id}/reactivate", async (Guid id, AppDbContext db) =>
{
    var result = await EquipmentCatalog.ReactivateAsync(id, db);
    return result.ToHttpResult(Results.Ok);
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter(Roles.Admin));

app.MapPost("/categories", async (AddCategoryRequest request, AppDbContext db) =>
{
    var result = await CategoryCatalog.CreateAsync(request.Name, db);
    return result.ToHttpResult(category => Results.Created($"/categories/{category.Id}", category));
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter(Roles.Admin));

app.MapGet("/categories", async (AppDbContext db) =>
    Results.Ok(await db.Categories.ToListAsync()));

app.MapGet("/me", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var email = principal.FindFirstValue(ClaimTypes.Email);
    var user = await UserAuthorization.FindAsync(email, db);

    if (user is null)
    {
        return Results.Json(
            "You are not authorized to access this application. Please contact your admin.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    return Results.Ok(new { user.Email, user.Role });
}).RequireAuthorization();

app.MapPost("/users", async (AddUserRequest request, ClaimsPrincipal principal, AppDbContext db) =>
{
    var anyAdminExists = await db.Users.AnyAsync(u => u.Role == Roles.Admin);
    if (anyAdminExists)
    {
        var caller = await UserAuthorization.FindAsync(principal.FindFirstValue(ClaimTypes.Email), db);
        if (UserAuthorization.RequireRole(caller, Roles.Admin) is { } denied)
        {
            return denied;
        }
    }

    var result = await UserCatalog.CreateAsync(request.Email, request.Role, db);
    return result.ToHttpResult(user => Results.Created($"/users/{user.Id}", user));
});

app.MapGet("/users", async (AppDbContext db) =>
    Results.Ok(await db.Users.ToListAsync()))
    .RequireAuthorization().AddEndpointFilter(new RequireRoleFilter(Roles.Admin));

app.MapPut("/users/{id}", async (Guid id, UpdateUserRoleRequest request, AppDbContext db) =>
{
    var result = await UserCatalog.UpdateRoleAsync(id, request.Role, db);
    return result.ToHttpResult(Results.Ok);
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter(Roles.Admin));

app.MapPost("/users/{id}/deactivate", async (Guid id, ClaimsPrincipal principal, AppDbContext db) =>
{
    var result = await UserCatalog.DeactivateAsync(id, principal.FindFirstValue(ClaimTypes.Email), db);
    return result.ToHttpResult(Results.Ok);
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter(Roles.Admin));

app.MapPost("/users/{id}/reactivate", async (Guid id, AppDbContext db) =>
{
    var result = await UserCatalog.ReactivateAsync(id, db);
    return result.ToHttpResult(Results.Ok);
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter(Roles.Admin));

app.MapPost("/equipment/import", async (IFormFile file, AppDbContext db, HttpRequest request) =>
{
    var updateExisting = bool.TryParse(request.Form["updateExisting"], out var parsed) && parsed;

    await using var stream = file.OpenReadStream();
    var result = await EquipmentImport.RunAsync(stream, db, updateExisting);
    return Results.Ok(result);
}).DisableAntiforgery().RequireAuthorization().AddEndpointFilter(new RequireRoleFilter(Roles.Admin, Roles.Operator));

app.Run();

public record CreateEquipmentRequest(string Name, string Category, string SerialNumber, string Site);
record AddDocumentRequest(string Label, string Url);
record AddCategoryRequest(string Name);
record AddUserRequest(string Email, string Role);
record UpdateUserRoleRequest(string Role);

public partial class Program;
