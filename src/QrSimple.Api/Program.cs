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

app.Run();

record CreateEquipmentRequest(string Name, string Category, string SerialNumber, string Site);

public partial class Program;
