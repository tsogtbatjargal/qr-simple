using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using QrSimple.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie()
    .AddGoogle(options =>
    {
        options.ClientId = builder.Configuration["Authentication:Google:ClientId"] ?? "";
        options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "";
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/equipment", async (CreateEquipmentRequest request, AppDbContext db) =>
{
    var result = await EquipmentCatalog.CreateAsync(request, db);
    return result.ToHttpResult(equipment => Results.Created($"/equipment/{equipment.Id}", equipment));
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter("Admin", "Operator"));

app.MapGet("/equipment", async (bool? includeRetired, AppDbContext db) =>
{
    var query = db.Equipment.AsQueryable();
    if (includeRetired != true)
    {
        query = query.Where(e => e.Status == EquipmentStatus.Active);
    }

    return Results.Ok(await query.ToListAsync());
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter("Admin", "Operator", "Reader"));

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
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter("Admin", "Operator"));

app.MapPut("/equipment/{id}", async (Guid id, CreateEquipmentRequest request, AppDbContext db) =>
{
    var result = await EquipmentCatalog.UpdateAsync(id, request, db);
    return result.ToHttpResult(Results.Ok);
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter("Admin", "Operator"));

app.MapPost("/equipment/{id}/retire", async (Guid id, AppDbContext db) =>
{
    var result = await EquipmentCatalog.RetireAsync(id, db);
    return result.ToHttpResult(Results.Ok);
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter("Admin", "Operator"));

app.MapPost("/equipment/{id}/reactivate", async (Guid id, AppDbContext db) =>
{
    var result = await EquipmentCatalog.ReactivateAsync(id, db);
    return result.ToHttpResult(Results.Ok);
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter("Admin", "Operator"));

app.MapPost("/categories", async (AddCategoryRequest request, AppDbContext db) =>
{
    var category = new Category { Id = Guid.NewGuid(), Name = request.Name };
    db.Categories.Add(category);
    await db.SaveChangesAsync();

    return Results.Created($"/categories/{category.Id}", category);
}).RequireAuthorization().AddEndpointFilter(new RequireRoleFilter("Admin"));

app.MapGet("/categories", async (AppDbContext db) =>
    Results.Ok(await db.Categories.ToListAsync()));

app.MapGet("/me", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var email = principal.FindFirstValue(ClaimTypes.Email);
    var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);

    if (user is null)
    {
        return Results.Json(
            "You are not authorized to access this application. Please contact your admin.",
            statusCode: StatusCodes.Status403Forbidden);
    }

    return Results.Ok(new { user.Email, user.Role });
}).RequireAuthorization();

app.MapPost("/users", async (AddUserRequest request, AppDbContext db) =>
{
    var user = new User { Id = Guid.NewGuid(), Email = request.Email, Role = request.Role };
    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created($"/users/{user.Id}", user);
});

app.MapGet("/users", async (AppDbContext db) =>
    Results.Ok(await db.Users.ToListAsync()));

app.MapPost("/equipment/import", async (IFormFile file, AppDbContext db, HttpRequest request) =>
{
    var updateExisting = bool.TryParse(request.Form["updateExisting"], out var parsed) && parsed;

    await using var stream = file.OpenReadStream();
    var result = await EquipmentImport.RunAsync(stream, db, updateExisting);
    return Results.Ok(result);
}).DisableAntiforgery().RequireAuthorization().AddEndpointFilter(new RequireRoleFilter("Admin", "Operator"));

app.Run();

public record CreateEquipmentRequest(string Name, string Category, string SerialNumber, string Site);
record AddDocumentRequest(string Label, string Url);
record AddCategoryRequest(string Name);
record AddUserRequest(string Email, string Role);

public partial class Program;
