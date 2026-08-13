using System.Net.Http.Json;

namespace QrSimple.Api.Tests;

public class ScanPageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Anyone_can_view_active_equipment_quick_info_without_logging_in()
    {
        var client = factory.CreateClientAs("Operator");

        var createResponse = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Drill Rig 7",
            category = "Drill",
            serialNumber = "DR-0007",
            site = "West Bench",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedEquipment>();

        var scanResponse = await client.GetAsync($"/e/{created!.Id}");

        Assert.Equal("text/html", scanResponse.Content.Headers.ContentType?.MediaType);
        var html = await scanResponse.Content.ReadAsStringAsync();
        Assert.Contains("Drill Rig 7", html);
        Assert.Contains("Drill", html);
        Assert.Contains("DR-0007", html);
        Assert.Contains("West Bench", html);
        Assert.Contains("name=\"viewport\"", html);
        Assert.Contains("Equipment details", html);
    }

    [Fact]
    public async Task Retired_equipment_scan_page_shows_no_longer_in_service_indicator()
    {
        var client = factory.CreateClientAs("Operator");

        var createResponse = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Old Pump",
            category = "Pump",
            serialNumber = "PM-0099",
            site = "East Pit",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedEquipment>();

        var retireResponse = await client.PostAsync($"/equipment/{created!.Id}/retire", content: null);
        Assert.True(retireResponse.IsSuccessStatusCode);

        var scanResponse = await client.GetAsync($"/e/{created.Id}");
        var html = await scanResponse.Content.ReadAsStringAsync();

        Assert.Contains("no longer in service", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Scan_page_shows_document_links_for_the_equipment()
    {
        var client = factory.CreateClientAs("Operator");

        var createResponse = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Excavator 4",
            category = "Excavator",
            serialNumber = "EX-0004",
            site = "North Pit",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedEquipment>();

        var addDocResponse = await client.PostAsJsonAsync($"/equipment/{created!.Id}/documents", new
        {
            label = "User Manual",
            url = "https://docs.example.com/ex-0004-manual.pdf",
        });
        Assert.True(addDocResponse.IsSuccessStatusCode);

        var scanResponse = await client.GetAsync($"/e/{created.Id}");
        var html = await scanResponse.Content.ReadAsStringAsync();

        Assert.Contains("User Manual", html);
        Assert.Contains("https://docs.example.com/ex-0004-manual.pdf", html);
        Assert.Contains("class=\"panel document\"", html);
        Assert.Contains("target=\"_blank\"", html);
    }

    [Fact]
    public async Task Scan_page_uses_equipment_photo_document_as_the_header_image()
    {
        var client = factory.CreateClientAs("Operator");

        var createResponse = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Pump 32",
            category = "Pump",
            serialNumber = "PUM-0032",
            site = "QA/QC",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedEquipment>();

        await client.PostAsJsonAsync($"/equipment/{created!.Id}/documents", new
        {
            label = "Equipment Photo",
            url = "https://images.example.com/pum-0032.png",
        });
        await client.PostAsJsonAsync($"/equipment/{created.Id}/documents", new
        {
            label = "Maintenance instruction",
            url = "https://docs.example.com/pum-0032-maintenance.pdf",
        });
        await client.PostAsJsonAsync($"/equipment/{created.Id}/documents", new
        {
            label = "User manual",
            url = "https://docs.example.com/pum-0032-manual.pdf",
        });

        var html = await client.GetStringAsync($"/e/{created.Id}");

        Assert.Contains("<img src=\"https://images.example.com/pum-0032.png\"", html);
        Assert.Contains("alt=\"Pump 32\"", html);
        Assert.DoesNotContain(">Equipment Photo</a>", html);
        Assert.Contains("Maintenance instruction", html);
        Assert.True(
            html.IndexOf("User manual", StringComparison.OrdinalIgnoreCase) <
            html.IndexOf("Maintenance instruction", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Reactivated_equipment_no_longer_shows_retired_indicator()
    {
        var client = factory.CreateClientAs("Operator");

        var createResponse = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Reactivated Pump",
            category = "Pump",
            serialNumber = "PM-0100",
            site = "East Pit",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedEquipment>();

        await client.PostAsync($"/equipment/{created!.Id}/retire", content: null);
        var reactivateResponse = await client.PostAsync($"/equipment/{created.Id}/reactivate", content: null);
        Assert.True(reactivateResponse.IsSuccessStatusCode);

        var scanResponse = await client.GetAsync($"/e/{created.Id}");
        var html = await scanResponse.Content.ReadAsStringAsync();

        Assert.DoesNotContain("no longer in service", html, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CreatedEquipment(Guid Id);
}
