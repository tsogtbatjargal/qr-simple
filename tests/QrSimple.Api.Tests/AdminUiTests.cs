using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace QrSimple.Api.Tests;

public class AdminUiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Unauthenticated_request_is_rejected()
    {
        // The test host's default auth scheme is TestAuthHandler (see ApiFactory), which
        // has no LoginPath/redirect behavior — it just 401s, same as every other protected
        // route in this app (see AuthTests/RoleGatingTests). A real browser using the actual
        // Cookie scheme gets redirected to LoginPath ("/login") instead.
        var client = factory.CreateClient();

        var response = await client.GetAsync("/app");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_sees_the_equipment_list()
    {
        var client = factory.CreateClientAs("Admin");

        var response = await client.GetAsync("/app");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Equipment", body);
        Assert.Contains("Add equipment", body);
    }

    [Fact]
    public async Task Unregistered_email_sees_the_not_authorized_page()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, "stranger-ui@example.com");

        var response = await client.GetAsync("/app");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Not authorized", body);
    }

    [Fact]
    public async Task Reader_cannot_reach_the_add_equipment_page()
    {
        var readerEmail = $"reader-ui-{Guid.NewGuid():N}@example.com";
        await factory.CreateClient().PostAsJsonAsync("/users", new { email = readerEmail, role = "Reader" });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, readerEmail);

        var response = await client.GetAsync("/app/equipment/add");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("http://localhost/app/not-authorized", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Equipment_detail_page_shows_the_qr_image_tag()
    {
        var client = factory.CreateClientAs("Admin");
        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name = "UI Detail Truck",
            category = "Truck",
            serialNumber = "UID-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<EquipmentResponse>();

        var response = await client.GetAsync($"/app/equipment/{equipment!.Id}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("UI Detail Truck", body);
        Assert.Contains($"/equipment/{equipment.Id}/qr", body);
    }

    [Fact]
    public async Task Admin_sees_a_full_edit_form_and_a_status_button_on_equipment_detail_page()
    {
        var client = factory.CreateClientAs("Admin");
        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Admin Edit Truck",
            category = "Truck",
            serialNumber = "AET-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<EquipmentResponse>();

        var response = await client.GetAsync($"/app/equipment/{equipment!.Id}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Name <input", body);
        Assert.Contains("Retire", body);
    }

    [Fact]
    public async Task Operator_sees_a_restricted_edit_form_and_no_status_button_on_equipment_detail_page()
    {
        var adminClient = factory.CreateClientAs("Admin");
        var created = await adminClient.PostAsJsonAsync("/equipment", new
        {
            name = "Operator View Truck",
            category = "Truck",
            serialNumber = "OVT-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<EquipmentResponse>();

        var operatorClient = factory.CreateClientAs("Operator");
        var response = await operatorClient.GetAsync($"/app/equipment/{equipment!.Id}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Name <input", body);
        Assert.DoesNotContain("Retire", body);
        Assert.DoesNotContain("Reactivate", body);
        Assert.Contains("Site", body);
    }

    [Fact]
    public async Task Equipment_detail_page_shows_photo_and_documents_sections()
    {
        var client = factory.CreateClientAs("Admin");
        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Documented Truck",
            category = "Truck",
            serialNumber = "DOC-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<EquipmentResponse>();

        Document photo;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var result = await DocumentCatalog.SetPhotoUploadAsync(
                equipment!.Id, TestUploads.TinyPngBytes, "image/png", "doc-0001.png", db);
            photo = ((DocumentResult.Success)result).Document;
        }

        using var docContent = TestUploads.Document(label: "User manual");
        var docCreated = await client.PostAsync($"/equipment/{equipment!.Id}/documents", docContent);
        var document = await docCreated.Content.ReadFromJsonAsync<DocumentResponse>();

        var response = await client.GetAsync($"/app/equipment/{equipment.Id}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Photo", body);
        Assert.Contains($"/documents/{photo.Id}/content", body);
        Assert.Contains("Manuals", body);
        // Not the row's "User manual" label text: that string is this test's own upload label
        // (and remains valid Document data after the 2026-09-01 section rename to "Manuals"),
        // so matching it would pass even if the row never rendered. Pin the row's own link.
        Assert.Contains($"/documents/{document!.Id}/content", body);
        Assert.Contains("Add manual", body);
    }

    [Fact]
    public async Task Reader_sees_documents_but_no_manage_controls_on_equipment_detail_page()
    {
        var adminClient = factory.CreateClientAs("Admin");
        var created = await adminClient.PostAsJsonAsync("/equipment", new
        {
            name = "Reader View Truck",
            category = "Truck",
            serialNumber = "RVT-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<EquipmentResponse>();

        using var docContent = TestUploads.Document(label: "User manual");
        var docCreated = await adminClient.PostAsync($"/equipment/{equipment!.Id}/documents", docContent);
        var document = await docCreated.Content.ReadFromJsonAsync<DocumentResponse>();

        var readerClient = factory.CreateClientAs("Reader");
        var response = await readerClient.GetAsync($"/app/equipment/{equipment.Id}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("Manuals", body);
        Assert.Contains($"/documents/{document!.Id}/content", body);
        Assert.DoesNotContain("Add manual", body);
        Assert.DoesNotContain("type=\"file\"", body);
    }

    [Fact]
    public async Task Operator_sees_the_rebuild_form_on_the_rebuild_history_page()
    {
        var client = factory.CreateClientAs("Operator");
        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Rebuilds UI Truck",
            category = "Truck",
            serialNumber = "RUI-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<EquipmentResponse>();

        var response = await client.GetAsync($"/app/equipment/{equipment!.Id}/rebuilds");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Add rebuild record", body);
        Assert.Contains("type=\"file\"", body);
    }

    [Fact]
    public async Task Reader_sees_the_rebuild_history_page_read_only()
    {
        var adminClient = factory.CreateClientAs("Admin");
        var created = await adminClient.PostAsJsonAsync("/equipment", new
        {
            name = "Reader Rebuilds Truck",
            category = "Truck",
            serialNumber = "RRT-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<EquipmentResponse>();

        using var content = TestUploads.Rebuild(note: "Reader-visible rebuild.");
        await adminClient.PostAsync($"/equipment/{equipment!.Id}/rebuilds", content);

        var readerClient = factory.CreateClientAs("Reader");
        var response = await readerClient.GetAsync($"/app/equipment/{equipment.Id}/rebuilds");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Reader-visible rebuild.", body);
        Assert.DoesNotContain("Add rebuild record", body);
        Assert.DoesNotContain("type=\"file\"", body);
    }

    [Fact]
    public async Task Unregistered_email_is_redirected_from_the_rebuild_history_page()
    {
        var adminClient = factory.CreateClientAs("Admin");
        var created = await adminClient.PostAsJsonAsync("/equipment", new
        {
            name = "Stranger Rebuilds Truck",
            category = "Truck",
            serialNumber = "SRT-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<EquipmentResponse>();

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, "stranger-rebuilds@example.com");

        var response = await client.GetAsync($"/app/equipment/{equipment!.Id}/rebuilds");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("http://localhost/app/not-authorized", response.Headers.Location?.OriginalString);
    }

    private sealed record EquipmentResponse(Guid Id);

    private sealed record DocumentResponse(Guid Id);
}
