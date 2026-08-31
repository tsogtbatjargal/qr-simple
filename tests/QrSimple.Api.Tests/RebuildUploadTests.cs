using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace QrSimple.Api.Tests;

public class RebuildUploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Happy_path_upload_persists_and_can_be_listed()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Rebuild Happy Path Pump");

        using var content = TestUploads.Rebuild(note: "Full engine rebuild.");
        var response = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var list = await RebuildCatalog.ListAsync(equipment.Id, db);

        Assert.Single(list);
        Assert.Equal("Full engine rebuild.", list[0].Note);
        Assert.True(list[0].HasFile);
    }

    [Fact]
    public async Task A_record_can_be_filed_with_no_pdf_at_all()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "No Pdf Pump");

        using var content = TestUploads.Rebuild(note: "Rebuild done, paperwork to follow.", includeFile: false);
        var response = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var list = await RebuildCatalog.ListAsync(equipment.Id, db);

        Assert.Single(list);
        Assert.False(list[0].HasFile);
        Assert.Equal("Rebuild done, paperwork to follow.", list[0].Note);
    }

    [Fact]
    public async Task A_record_with_no_note_is_rejected_even_when_a_pdf_is_attached()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Blank Note Pump");

        using var content = TestUploads.Rebuild(note: "   ");
        var response = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Content_endpoint_404s_for_a_record_with_no_pdf()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Missing Pdf Pump");

        using var content = TestUploads.Rebuild(includeFile: false);
        var created = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);
        var rebuild = await created.Content.ReadFromJsonAsync<CreatedRebuild>();

        var response = await client.GetAsync($"/rebuilds/{rebuild!.Id}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_pdf_can_be_attached_to_a_record_that_has_none()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Attach Later Pump");

        using var content = TestUploads.Rebuild(includeFile: false);
        var created = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);
        var rebuild = await created.Content.ReadFromJsonAsync<CreatedRebuild>();

        using var attachment = TestUploads.OemReport(fileName: "late-report.pdf");
        var attachResponse = await client.PostAsync($"/rebuilds/{rebuild!.Id}/file", attachment);

        Assert.Equal(HttpStatusCode.OK, attachResponse.StatusCode);

        var contentResponse = await client.GetAsync($"/rebuilds/{rebuild.Id}/content");
        Assert.Equal(HttpStatusCode.OK, contentResponse.StatusCode);
        Assert.Equal(TestUploads.FakePdfBytes, await contentResponse.Content.ReadAsByteArrayAsync());
    }

    // Attach, never replace: swapping the PDF under an unchanged note and date is exactly the
    // ambiguity docs/plans/0002-inspection-records.md decision 13 exists to prevent.
    [Fact]
    public async Task Attaching_to_a_record_that_already_has_a_pdf_is_rejected()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "No Replace Pump");

        using var content = TestUploads.Rebuild();
        var created = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);
        var rebuild = await created.Content.ReadFromJsonAsync<CreatedRebuild>();

        using var attachment = TestUploads.OemReport(fileName: "replacement.pdf");
        var response = await client.PostAsync($"/rebuilds/{rebuild!.Id}/file", attachment);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_operator_cannot_attach_a_pdf_to_another_operators_record()
    {
        var uploaderClient = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(uploaderClient, "Cross Attach Pump");

        using var content = TestUploads.Rebuild(includeFile: false);
        var created = await uploaderClient.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);
        var rebuild = await created.Content.ReadFromJsonAsync<CreatedRebuild>();

        var otherOperatorClient = factory.CreateClientAs("Operator");
        using var attachment = TestUploads.OemReport();
        var response = await otherOperatorClient.PostAsync($"/rebuilds/{rebuild!.Id}/file", attachment);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_docx_is_rejected_even_though_it_is_a_valid_document_type()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Docx Rejected Pump");

        using var content = TestUploads.Rebuild(fileName: "report.docx", contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        var response = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_oversized_pdf_is_rejected()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Oversized Rebuild Pump");

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[DocumentUpload.MaxRebuildBytes + 1]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "huge.pdf");
        content.Add(new StringContent("Oversized."), "note");
        content.Add(new StringContent(BusinessTime.Today().ToString("yyyy-MM-dd")), "rebuildDate");

        var response = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_future_rebuild_date_is_rejected()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Future Date Pump");

        using var content = TestUploads.Rebuild(rebuildDate: BusinessTime.Today().AddDays(1));
        var response = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_rebuild_date_is_rejected()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "No Date Pump");

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent("Note without a date."), "note");

        var response = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_to_retired_equipment_is_rejected()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(operatorClient, "Retired Upload Pump");

        var adminClient = factory.CreateClientAs("Admin");
        var retireResponse = await adminClient.PostAsync($"/equipment/{equipment.Id}/retire", content: null);
        Assert.True(retireResponse.IsSuccessStatusCode);

        using var content = TestUploads.Rebuild();
        var response = await operatorClient.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_for_unknown_equipment_returns_not_found()
    {
        var client = factory.CreateClientAs("Operator");

        using var content = TestUploads.Rebuild();
        var response = await client.PostAsync($"/equipment/{Guid.NewGuid()}/rebuilds", content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadedByEmail_is_captured_from_the_authenticated_caller()
    {
        var email = $"operator-rebuild-{Guid.NewGuid():N}@example.com";
        var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User { Id = Guid.NewGuid(), Email = email, Role = Roles.Operator });
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, email);

        var equipment = await CreateEquipmentAsync(client, "Uploader Email Pump");

        using var content = TestUploads.Rebuild();
        await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var list = await RebuildCatalog.ListAsync(equipment.Id, db2);

        Assert.Equal(email, list[0].UploadedByEmail);
    }

    [Fact]
    public async Task Listing_is_ordered_by_rebuild_date_not_upload_order()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Order Flip Pump");

        // Upload the later-dated record first, then an earlier one second — list order must
        // follow RebuildDate, not upload sequence.
        using var later = TestUploads.Rebuild(rebuildDate: BusinessTime.Today());
        await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", later);

        using var earlier = TestUploads.Rebuild(rebuildDate: BusinessTime.Today().AddMonths(-2));
        await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", earlier);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var list = await RebuildCatalog.ListAsync(equipment.Id, db);

        Assert.Equal(2, list.Count);
        Assert.Equal(BusinessTime.Today(), list[0].RebuildDate);
        Assert.Equal(BusinessTime.Today().AddMonths(-2), list[1].RebuildDate);
    }

    [Fact]
    public async Task Content_endpoint_serves_bytes_inline_with_a_generated_filename()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Inline Disposition Pump");

        using var content = TestUploads.Rebuild(rebuildDate: new DateOnly(2026, 8, 12));
        var uploadResponse = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);
        var rebuild = await uploadResponse.Content.ReadFromJsonAsync<CreatedRebuild>();

        var contentResponse = await client.GetAsync($"/rebuilds/{rebuild!.Id}/content");

        Assert.Equal(HttpStatusCode.OK, contentResponse.StatusCode);
        Assert.Equal("application/pdf", contentResponse.Content.Headers.ContentType?.MediaType);

        var disposition = contentResponse.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("inline", disposition!.DispositionType);
        Assert.Contains("Rebuild-2026-08-12.pdf", disposition.FileName);
        Assert.NotNull(disposition.FileNameStar);

        var bytes = await contentResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(TestUploads.FakePdfBytes, bytes);
    }

    [Fact]
    public async Task Content_endpoint_requires_no_authentication()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Anon Rebuild Pump");

        using var content = TestUploads.Rebuild();
        var uploadResponse = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);
        var rebuild = await uploadResponse.Content.ReadFromJsonAsync<CreatedRebuild>();

        var anonymousClient = factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/rebuilds/{rebuild!.Id}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Content_endpoint_404s_for_an_unknown_rebuild()
    {
        var client = factory.CreateClientAs("Operator");

        var response = await client.GetAsync($"/rebuilds/{Guid.NewGuid()}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<CreatedEquipment> CreateEquipmentAsync(HttpClient client, string name)
    {
        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name,
            category = "Pump",
            serialNumber = $"RB-{Guid.NewGuid():N}"[..12],
            site = "North Pit",
        });
        return (await created.Content.ReadFromJsonAsync<CreatedEquipment>())!;
    }

    private sealed record CreatedEquipment(Guid Id);
    private sealed record CreatedRebuild(Guid Id);
}
