using SkiaSharp;

namespace Clinic.API;

internal static class CaptchaImageGenerator
{
    public static byte[] Create(string code)
    {
        using var bitmap = new SKBitmap(240, 80);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(246, 250, 252));

        using var font = new SKFont(SKTypeface.Default, 40);
        using var paint = new SKPaint
        {
            Color = new SKColor(19, 73, 102),
            IsAntialias = true
        };

        for (var i = 0; i < code.Length; i++)
        {
            var x = 22 + i * 42;
            var y = Random.Shared.Next(49, 64);
            canvas.DrawText(code[i].ToString(), x, y, font, paint);
        }

        // Линии немного мешают автоматическому распознаванию текста.
        paint.Color = new SKColor(100, 150, 170, 100);
        paint.StrokeWidth = 1.5f;
        for (var i = 0; i < 6; i++)
        {
            canvas.DrawLine(Random.Shared.Next(240), Random.Shared.Next(80),
                Random.Shared.Next(240), Random.Shared.Next(80), paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
}
