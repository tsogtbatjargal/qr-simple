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

    // `includeFile: false` posts a rebuild with no PDF part at all — the case the optional-PDF
    // change exists for. Note defaults to something non-empty because the note is required.
    public static MultipartFormDataContent Rebuild(
        DateOnly? rebuildDate = null,
        string note = "Rebuild completed.",
        string fileName = "rebuild.pdf",
        byte[]? bytes = null,
        string contentType = "application/pdf",
        bool includeFile = true)
    {
        var content = new MultipartFormDataContent();
        if (includeFile)
        {
            var fileContent = new ByteArrayContent(bytes ?? FakePdfBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(fileContent, "file", fileName);
        }
        content.Add(new StringContent((rebuildDate ?? BusinessTime.Today()).ToString("yyyy-MM-dd")), "rebuildDate");
        content.Add(new StringContent(note), "note");
        return content;
    }

    // The OEM QA/QC report has its own single-slot endpoint, so unlike Document() this carries
    // no label — the label is fixed server-side.
    public static MultipartFormDataContent OemReport(
        string fileName = "oem-qa-qc.pdf",
        byte[]? bytes = null,
        string contentType = "application/pdf")
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes ?? FakePdfBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }
}
