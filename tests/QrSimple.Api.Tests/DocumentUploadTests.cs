using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace QrSimple.Api.Tests;

public class DocumentUploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Uploading_an_oversized_document_is_rejected()
    {
        var client = factory.CreateClientAs("Operator");
        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Oversized Doc Truck",
            category = "Truck",
            serialNumber = "OD-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<CreatedEquipment>();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(new byte[DocumentUpload.MaxDocumentBytes + 1]);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", "huge.pdf");

        var response = await client.PostAsync($"/equipment/{equipment!.Id}/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_a_document_with_an_unsupported_extension_is_rejected()
    {
        var client = factory.CreateClientAs("Operator");
        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Bad Extension Truck",
            category = "Truck",
            serialNumber = "BE-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<CreatedEquipment>();

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("hello"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "notes.txt");

        var response = await client.PostAsync($"/equipment/{equipment!.Id}/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Uploading_a_document_for_unknown_equipment_returns_not_found()
    {
        var client = factory.CreateClientAs("Operator");

        using var content = TestUploads.Document(label: "User Manual");
        var response = await client.PostAsync($"/equipment/{Guid.NewGuid()}/documents", content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Content_endpoint_returns_the_uploaded_bytes_and_content_type()
    {
        var client = factory.CreateClientAs("Operator");
        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Content Endpoint Truck",
            category = "Truck",
            serialNumber = "CE-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<CreatedEquipment>();

        using var docContent = TestUploads.Document(label: "User Manual");
        var addResponse = await client.PostAsync($"/equipment/{equipment!.Id}/documents", docContent);
        var document = await addResponse.Content.ReadFromJsonAsync<CreatedDocument>();

        var contentResponse = await client.GetAsync($"/documents/{document!.Id}/content");

        Assert.Equal(HttpStatusCode.OK, contentResponse.StatusCode);
        Assert.Equal("application/pdf", contentResponse.Content.Headers.ContentType?.MediaType);
        var downloadedBytes = await contentResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(TestUploads.FakePdfBytes, downloadedBytes);
    }

    [Fact]
    public async Task Content_endpoint_requires_no_authentication()
    {
        var client = factory.CreateClientAs("Operator");
        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Anon Content Truck",
            category = "Truck",
            serialNumber = "AC-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<CreatedEquipment>();

        using var docContent = TestUploads.Document(label: "User Manual");
        var addResponse = await client.PostAsync($"/equipment/{equipment!.Id}/documents", docContent);
        var document = await addResponse.Content.ReadFromJsonAsync<CreatedDocument>();

        var anonymousClient = factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/documents/{document!.Id}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Content_endpoint_404s_for_an_unknown_document()
    {
        var client = factory.CreateClientAs("Operator");

        var response = await client.GetAsync($"/documents/{Guid.NewGuid()}/content");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Replacing_the_photo_overwrites_the_existing_row_instead_of_adding_a_second_one()
    {
        var client = factory.CreateClientAs("Operator");
        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Photo Replace Truck",
            category = "Truck",
            serialNumber = "PR-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<CreatedEquipment>();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var first = await DocumentCatalog.SetPhotoUploadAsync(
            equipment!.Id, TestUploads.TinyPngBytes, "image/png", "first.png", db);
        var second = await DocumentCatalog.SetPhotoUploadAsync(
            equipment.Id, TestUploads.TinyPngBytes, "image/png", "second.png", db);

        Assert.IsType<DocumentResult.Success>(first);
        Assert.IsType<DocumentResult.Success>(second);

        var photoRows = db.Documents
            .Where(d => d.EquipmentId == equipment.Id)
            .AsEnumerable()
            .Where(d => DocumentCatalog.IsPhotoLabel(d.Label))
            .ToList();

        Assert.Single(photoRows);
        Assert.Equal("second.png", photoRows[0].FileName);
    }

    private sealed record CreatedEquipment(Guid Id);
    private sealed record CreatedDocument(Guid Id);
}
