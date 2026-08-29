using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

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

        var adminClient = factory.CreateClientAs("Admin");
        var retireResponse = await adminClient.PostAsync($"/equipment/{created!.Id}/retire", content: null);
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

        using var docContent = TestUploads.Document(label: "User Manual");
        var addDocResponse = await client.PostAsync($"/equipment/{created!.Id}/documents", docContent);
        Assert.True(addDocResponse.IsSuccessStatusCode);
        var document = await addDocResponse.Content.ReadFromJsonAsync<CreatedDocument>();

        var scanResponse = await client.GetAsync($"/e/{created.Id}");
        var html = await scanResponse.Content.ReadAsStringAsync();

        Assert.Contains("User Manual", html);
        Assert.Contains($"/documents/{document!.Id}/content", html);
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

        Document photo;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var result = await DocumentCatalog.SetPhotoUploadAsync(
                created!.Id, TestUploads.TinyPngBytes, "image/png", "pump-32.png", db);
            photo = ((DocumentResult.Success)result).Document;
        }

        using var maintenanceContent = TestUploads.Document(fileName: "maintenance.pdf", label: "Maintenance instruction");
        await client.PostAsync($"/equipment/{created.Id}/documents", maintenanceContent);

        using var manualContent = TestUploads.Document(fileName: "manual.pdf", label: "User manual");
        await client.PostAsync($"/equipment/{created.Id}/documents", manualContent);

        var html = await client.GetStringAsync($"/e/{created.Id}");

        Assert.Contains($"<img src=\"/documents/{photo.Id}/content\"", html);
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

        var adminClient = factory.CreateClientAs("Admin");
        await adminClient.PostAsync($"/equipment/{created!.Id}/retire", content: null);
        var reactivateResponse = await adminClient.PostAsync($"/equipment/{created.Id}/reactivate", content: null);
        Assert.True(reactivateResponse.IsSuccessStatusCode);

        var scanResponse = await client.GetAsync($"/e/{created.Id}");
        var html = await scanResponse.Content.ReadAsStringAsync();

        Assert.DoesNotContain("no longer in service", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_a_document_removes_it_from_the_scan_page()
    {
        var client = factory.CreateClientAs("Operator");

        var createResponse = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Loader 9",
            category = "Loader",
            serialNumber = "LD-0009",
            site = "West Bench",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedEquipment>();

        using var docContent = TestUploads.Document(label: "User Manual");
        var addDocResponse = await client.PostAsync($"/equipment/{created!.Id}/documents", docContent);
        var document = await addDocResponse.Content.ReadFromJsonAsync<CreatedDocument>();

        var deleteResponse = await client.DeleteAsync($"/equipment/{created.Id}/documents/{document!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var html = await client.GetStringAsync($"/e/{created.Id}");
        Assert.DoesNotContain("User Manual", html);
    }

    [Fact]
    public async Task Deleting_an_unknown_document_returns_not_found()
    {
        var client = factory.CreateClientAs("Operator");

        var response = await client.DeleteAsync($"/equipment/{Guid.NewGuid()}/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // Equipment with inspection records but no Documents must not show the "no documents"
    // empty state — the inspections panel alone is enough content for the .documents nav.
    [Fact]
    public async Task Scan_page_does_not_show_no_documents_message_when_only_inspections_exist()
    {
        var client = factory.CreateClientAs("Operator");

        var createResponse = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Inspection Only Pump",
            category = "Pump",
            serialNumber = "IOP-0001",
            site = "North Pit",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedEquipment>();

        using var inspectionContent = TestUploads.Inspection();
        var uploadResponse = await client.PostAsync($"/equipment/{created!.Id}/inspections", inspectionContent);
        Assert.True(uploadResponse.IsSuccessStatusCode);

        var html = await client.GetStringAsync($"/e/{created.Id}");

        Assert.DoesNotContain("No documents are available", html);
        Assert.Contains("Inspection records (1)", html);
    }

    private sealed record CreatedEquipment(Guid Id);
    private sealed record CreatedDocument(Guid Id);
}
