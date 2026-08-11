using SkiaSharp;
using ZXing;
using ZXing.QrCode;
using ZXing.SkiaSharp;
using ZXing.SkiaSharp.Rendering;

namespace QrSimple.Api;

public static class QrCode
{
    public static byte[] GeneratePng(string content)
    {
        var writer = new BarcodeWriter<SKBitmap>
        {
            Format = BarcodeFormat.QR_CODE,
            Renderer = new SKBitmapRenderer(),
            Options = new QrCodeEncodingOptions
            {
                Width = 300,
                Height = 300,
                Margin = 1,
            },
        };

        using var bitmap = writer.Write(content);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
