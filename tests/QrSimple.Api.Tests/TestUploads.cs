using System.Net.Http.Headers;
using System.Text;

namespace QrSimple.Api.Tests;

internal static class TestUploads
{
    // A minimal valid 1x1 transparent PNG — small, passes photo extension/content-type
    // validation without needing a real image library in the test project.
    public static readonly byte[] TinyPngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    public static readonly byte[] FakePdfBytes = Encoding.UTF8.GetBytes("%PDF-1.4 fake pdf body for tests");

    // There is no HTTP route for setting the equipment photo — DocumentCatalog.SetPhotoUploadAsync
    // is only called in-process by EquipmentDetail.razor's Photo section (see AGENTS.md's
    // DocumentCatalog bullet). Tests that need a photo document seeded call SetPhotoUploadAsync
    // directly via a scoped AppDbContext (see DocumentUploadTests for the established pattern),
    // not this multipart helper — the generic POST /equipment/{id}/documents endpoint only
    // accepts document extensions (.pdf/.doc/.docx/.xls/.xlsx), so a .png upload through it
    // would always be rejected with 400.
    public static MultipartFormDataContent Document(string fileName = "manual.pdf", string? label = null)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(FakePdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", fileName);
        if (label is not null)
        {
            content.Add(new StringContent(label), "label");
        }
        return content;
    }

    public static MultipartFormDataContent Inspection(
        string kind = InspectionKinds.Monthly,
        DateOnly? inspectionDate = null,
        string? note = null,
        string fileName = "inspection.pdf",
        byte[]? bytes = null,
        string contentType = "application/pdf")
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes ?? FakePdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(kind), "kind");
        content.Add(new StringContent((inspectionDate ?? BusinessTime.Today()).ToString("yyyy-MM-dd")), "inspectionDate");
        if (note is not null)
        {
            content.Add(new StringContent(note), "note");
        }
        return content;
    }
}
