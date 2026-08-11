using System.Net.Http.Json;
using SkiaSharp;
using ZXing;
using ZXing.SkiaSharp;

namespace QrSimple.Api.Tests;

public class QrCodeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Qr_code_is_available_immediately_after_equipment_is_created()
    {
        var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Conveyor 3",
            category = "Conveyor",
            serialNumber = "CV-0003",
            site = "South Pit",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedEquipment>();

        var qrResponse = await client.GetAsync($"/equipment/{created!.Id}/qr");

        Assert.Equal("image/png", qrResponse.Content.Headers.ContentType?.MediaType);

        var bytes = await qrResponse.Content.ReadAsByteArrayAsync();
        using var bitmap = SKBitmap.Decode(bytes);
        var reader = new BarcodeReader<SKBitmap>(bmp => new SKBitmapLuminanceSource(bmp));
        var result = reader.Decode(bitmap);

        Assert.NotNull(result);
        Assert.Equal($"https://qr-simple.test/e/{created.Id}", result.Text);
    }

    private sealed record CreatedEquipment(Guid Id);
}
