using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace QrSimple.Api.Tests;

public class InspectionUploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Happy_path_upload_persists_and_can_be_listed()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Inspection Happy Path Pump");

        using var content = TestUploads.Inspection(kind: InspectionKinds.Quarterly, note: "All clear.");
        var response = await client.PostAsync($"/equipment/{equipment.Id}/inspections", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var list = await InspectionCatalog.ListAsync(equipment.Id, db);

        Assert.Single(list);
        Assert.Equal(InspectionKinds.Quarterly, list[0].Kind);
        Assert.Equal("All clear.", list[0].Note);
    }

    [Fact]
    public async Task A_docx_is_rejected_even_though_it_is_a_valid_document_type()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Docx Rejected Pump");

        using var content = TestUploads.Inspection(fileName: "report.docx", contentType: "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        var response = await client.PostAsync($"/equipment/{equipment.Id}/inspections", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_oversized_pdf_is_rejected()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Oversized Inspection Pump");

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[DocumentUpload.MaxInspectionBytes + 1]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "huge.pdf");
        content.Add(new StringContent(InspectionKinds.Monthly), "kind");
        content.Add(new StringContent(BusinessTime.Today().ToString("yyyy-MM-dd")), "inspectionDate");

        var response = await client.PostAsync($"/equipment/{equipment.Id}/inspections", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_kind_is_rejected()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Unknown Kind Pump");

        using var content = TestUploads.Inspection(kind: "Biannual");
        var response = await client.PostAsync($"/equipment/{equipment.Id}/inspections", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_future_inspection_date_is_rejected()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Future Date Pump");

        using var content = TestUploads.Inspection(inspectionDate: BusinessTime.Today().AddDays(1));
        var response = await client.PostAsync($"/equipment/{equipment.Id}/inspections", content);

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

        using var content = TestUploads.Inspection();
        var response = await operatorClient.PostAsync($"/equipment/{equipment.Id}/inspections", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_for_unknown_equipment_returns_not_found()
    {
        var client = factory.CreateClientAs("Operator");

        using var content = TestUploads.Inspection();
        var response = await client.PostAsync($"/equipment/{Guid.NewGuid()}/inspections", content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UploadedByEmail_is_captured_from_the_authenticated_caller()
    {
        var email = $"operator-inspect-{Guid.NewGuid():N}@example.com";
        var client = factory.CreateClient();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User { Id = Guid.NewGuid(), Email = email, Role = Roles.Operator });
            await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, email);

        var equipment = await CreateEquipmentAsync(client, "Uploader Email Pump");

        using var content = TestUploads.Inspection();
        await client.PostAsync($"/equipment/{equipment.Id}/inspections", content);

        using var scope2 = factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<AppDbContext>();
        var list = await InspectionCatalog.ListAsync(equipment.Id, db2);

        Assert.Equal(email, list[0].UploadedByEmail);
    }

    [Fact]
    public async Task Listing_is_ordered_by_inspection_date_not_upload_order()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Order Flip Pump");

        // Upload the earlier-dated inspection first, then an even-earlier one second — list
        // order must follow InspectionDate, not upload sequence.
        using var later = TestUploads.Inspection(inspectionDate: BusinessTime.Today());
        await client.PostAsync($"/equipment/{equipment.Id}/inspections", later);

        using var earlier = TestUploads.Inspection(inspectionDate: BusinessTime.Today().AddMonths(-2));
        await client.PostAsync($"/equipment/{equipment.Id}/inspections", earlier);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var list = await InspectionCatalog.ListAsync(equipment.Id, db);

        Assert.Equal(2, list.Count);
        Assert.Equal(BusinessTime.Today(), list[0].InspectionDate);
        Assert.Equal(BusinessTime.Today().AddMonths(-2), list[1].InspectionDate);
    }

    [Fact]
    public async Task Content_endpoint_serves_bytes_inline_with_a_generated_filename()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Inline Disposition Pump");

        using var content = TestUploads.Inspection(kind: InspectionKinds.Annual, inspectionDate: new DateOnly(2026, 8, 12));
        var uploadResponse = await client.PostAsync($"/equipment/{equipment.Id}/inspections", content);
        var inspection = await uploadResponse.Content.ReadFromJsonAsync<CreatedInspection>();

        var contentResponse = await client.GetAsync($"/inspections/{inspection!.Id}/content");

        Assert.Equal(HttpStatusCode.OK, contentResponse.StatusCode);
        Assert.Equal("application/pdf", contentResponse.Content.Headers.ContentType?.MediaType);

        var disposition = contentResponse.Content.Headers.ContentDisposition;
        Assert.NotNull(disposition);
        Assert.Equal("inline", disposition!.DispositionType);
        Assert.Contains("Annual-2026-08-12.pdf", disposition.FileName);
        Assert.NotNull(disposition.FileNameStar);

        var bytes = await contentResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(TestUploads.FakePdfBytes, bytes);
    }

    [Fact]
    public async Task Content_endpoint_requires_no_authentication()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Anon Inspection Pump");

        using var content = TestUploads.Inspection();
        var uploadResponse = await client.PostAsync($"/equipment/{equipment.Id}/inspections", content);
        var inspection = await uploadResponse.Content.ReadFromJsonAsync<CreatedInspection>();

        var anonymousClient = factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/inspections/{inspection!.Id}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Content_endpoint_404s_for_an_unknown_inspection()
    {
        var client = factory.CreateClientAs("Operator");

        var response = await client.GetAsync($"/inspections/{Guid.NewGuid()}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<CreatedEquipment> CreateEquipmentAsync(HttpClient client, string name)
    {
        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name,
            category = "Pump",
            serialNumber = $"INS-{Guid.NewGuid():N}"[..12],
            site = "North Pit",
        });
        return (await created.Content.ReadFromJsonAsync<CreatedEquipment>())!;
    }

    private sealed record CreatedEquipment(Guid Id);
    private sealed record CreatedInspection(Guid Id);
}
