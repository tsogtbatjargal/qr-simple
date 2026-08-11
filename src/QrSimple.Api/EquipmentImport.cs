using System.Globalization;
using CsvHelper;
using Microsoft.EntityFrameworkCore;

namespace QrSimple.Api;

public sealed class EquipmentCsvRow
{
    public required string Name { get; set; }
    public required string Category { get; set; }
    public required string SerialNumber { get; set; }
    public required string Site { get; set; }
}

public sealed record SkippedRow(string SerialNumber, string Reason);

public sealed record ImportResult(
    int CreatedCount,
    int SkippedCount,
    int UpdatedCount,
    List<string> CreatedSerialNumbers,
    List<SkippedRow> Skipped);

public static class EquipmentImport
{
    public static async Task<ImportResult> RunAsync(Stream csvStream, AppDbContext db, bool updateExisting = false)
    {
        using var reader = new StreamReader(csvStream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var rows = csv.GetRecords<EquipmentCsvRow>().ToList();

        var existingBySerialNumber = await db.Equipment.ToDictionaryAsync(e => e.SerialNumber);
        var knownCategories = (await db.Categories.Select(c => c.Name).ToListAsync()).ToHashSet();

        var createdSerialNumbers = new List<string>();
        var skipped = new List<SkippedRow>();
        var updatedCount = 0;

        foreach (var row in rows)
        {
            if (!knownCategories.Contains(row.Category))
            {
                skipped.Add(new SkippedRow(row.SerialNumber, $"Unknown category: {row.Category}"));
                continue;
            }

            if (existingBySerialNumber.TryGetValue(row.SerialNumber, out var existing))
            {
                if (!updateExisting)
                {
                    skipped.Add(new SkippedRow(row.SerialNumber, "Serial/Asset Number already exists"));
                    continue;
                }

                existing.Name = row.Name;
                existing.Category = row.Category;
                existing.Site = row.Site;
                updatedCount++;
                continue;
            }

            var equipment = Equipment.Create(row.Name, row.Category, row.SerialNumber, row.Site);

            db.Equipment.Add(equipment);
            createdSerialNumbers.Add(row.SerialNumber);
            existingBySerialNumber.Add(row.SerialNumber, equipment);
        }

        await db.SaveChangesAsync();

        return new ImportResult(createdSerialNumbers.Count, skipped.Count, updatedCount, createdSerialNumbers, skipped);
    }
}
