using Microsoft.EntityFrameworkCore;
using QrSimple.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

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

app.MapPost("/equipment", async (CreateEquipmentRequest request, AppDbContext db) =>
{
    var equipment = new Equipment
    {
        Id = Guid.NewGuid(),
        Name = request.Name,
        Category = request.Category,
        SerialNumber = request.SerialNumber,
        Site = request.Site,
    };

    db.Equipment.Add(equipment);
    await db.SaveChangesAsync();

    return Results.Created($"/equipment/{equipment.Id}", equipment);
});

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
});

app.MapPost("/equipment/{id}/retire", async (Guid id, AppDbContext db) =>
{
    var equipment = await db.Equipment.FindAsync(id);
    if (equipment is null)
    {
        return Results.NotFound();
    }

    equipment.Status = "Retired";
    await db.SaveChangesAsync();

    return Results.Ok(equipment);
});

app.MapPost("/equipment/import", async (IFormFile file, AppDbContext db, HttpRequest request) =>
{
    var updateExisting = bool.TryParse(request.Form["updateExisting"], out var parsed) && parsed;

    await using var stream = file.OpenReadStream();
    var result = await EquipmentImport.RunAsync(stream, db, updateExisting);
    return Results.Ok(result);
}).DisableAntiforgery();

app.Run();

record CreateEquipmentRequest(string Name, string Category, string SerialNumber, string Site);
record AddDocumentRequest(string Label, string Url);

public partial class Program;
