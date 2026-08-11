using System.Net.Http.Json;
using System.Text;

namespace QrSimple.Api.Tests;

public class CsvImportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Operator_can_bulk_import_new_equipment_from_csv()
    {
        var client = factory.CreateClient();

        var csv = """
            Name,Category,SerialNumber,Site
            Haul Truck 20,Truck,HT-0020,North Pit
            Haul Truck 21,Truck,HT-0021,North Pit
            """;

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "equipment.csv");

        var response = await client.PostAsync("/equipment/import", content);

        Assert.True(response.IsSuccessStatusCode);
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();

        Assert.NotNull(result);
        Assert.Equal(2, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Contains("HT-0020", result.CreatedSerialNumbers);
        Assert.Contains("HT-0021", result.CreatedSerialNumbers);
    }

    [Fact]
    public async Task Duplicate_serial_number_is_skipped_and_reported_not_overwritten()
    {
        var client = factory.CreateClient();

        var original = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Original Name",
            category = "Truck",
            serialNumber = "HT-0030",
            site = "North Pit",
        });
        var createdOriginal = await original.Content.ReadFromJsonAsync<CreatedEquipment>();

        var csv = """
            Name,Category,SerialNumber,Site
            Attempted Overwrite,Truck,HT-0030,South Pit
            Haul Truck 31,Truck,HT-0031,North Pit
            """;

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "equipment.csv");

        var response = await client.PostAsync("/equipment/import", content);
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();

        Assert.NotNull(result);
        Assert.Equal(1, result.CreatedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Contains("HT-0031", result.CreatedSerialNumbers);
        Assert.Contains(result.Skipped, s => s.SerialNumber == "HT-0030");

        var scanResponse = await client.GetAsync($"/e/{createdOriginal!.Id}");
        var html = await scanResponse.Content.ReadAsStringAsync();
        Assert.Contains("Original Name", html);
        Assert.DoesNotContain("Attempted Overwrite", html);
    }

    [Fact]
    public async Task UpdateExisting_mode_upserts_matching_serial_number_instead_of_skipping()
    {
        var client = factory.CreateClient();

        var original = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Stale Name",
            category = "Truck",
            serialNumber = "HT-0040",
            site = "North Pit",
        });
        var createdOriginal = await original.Content.ReadFromJsonAsync<CreatedEquipment>();

        var csv = """
            Name,Category,SerialNumber,Site
            Corrected Name,Truck,HT-0040,South Pit
            """;

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "file", "equipment.csv");
        content.Add(new StringContent("true"), "updateExisting");

        var response = await client.PostAsync("/equipment/import", content);
        var result = await response.Content.ReadFromJsonAsync<ImportResult>();

        Assert.NotNull(result);
        Assert.Equal(0, result.CreatedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(1, result.UpdatedCount);

        var scanResponse = await client.GetAsync($"/e/{createdOriginal!.Id}");
        var html = await scanResponse.Content.ReadAsStringAsync();
        Assert.Contains("Corrected Name", html);
        Assert.Contains("South Pit", html);
        Assert.DoesNotContain("Stale Name", html);
    }

    private sealed record CreatedEquipment(Guid Id);
    private sealed record SkippedRow(string SerialNumber, string Reason);
    private sealed record ImportResult(
        int CreatedCount,
        int SkippedCount,
        int UpdatedCount,
        List<string> CreatedSerialNumbers,
        List<SkippedRow> Skipped);
}
