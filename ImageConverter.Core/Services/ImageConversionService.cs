using System.IO;
using SkiaSharp;

namespace ImageConverter.Core.Services;

public static class ImageConversionService
{
    private const int ThumbnailSize = 120;
    private const long HdPixels = 1920L * 1080;
    private const long FourKPixels = 3840L * 2160;

    public static readonly HashSet<string> SupportedExtensions = new(
        new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" },
        StringComparer.OrdinalIgnoreCase);

    public static Task<(bool success, string error)> ConvertAsync(string filePath, int webpQuality, bool removeExif, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var thumbnailOrigin = ReadOrientation(filePath);
                // Note: When removeExif is false, the WebP output uses TopLeft origin.
                // SkiaSharp's WebP encoder does not record EXIF orientation, so an image
                // that was rotated via EXIF may appear un-rotated in the resulting WebP.
                var webpOrigin = removeExif ? thumbnailOrigin : SKEncodedOrigin.TopLeft;
                GenerateThumbnail(filePath, thumbnailOrigin);
                GenerateWebp(filePath, webpQuality, webpOrigin);
                return (true, "");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (false, ex.Message);
            }
        }, ct);
    }

    public static bool IsSupportedFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }

    public static int CalculateAutoQuality(string filePath)
    {
        using var codec = SKCodec.Create(filePath);
        if (codec is null) return 90;

        long pixels = (long)codec.Info.Width * codec.Info.Height;

        if (pixels <= HdPixels) return 90;
        if (pixels >= FourKPixels) return 70;

        double ratio = (double)(FourKPixels - pixels) / (FourKPixels - HdPixels);
        return (int)Math.Round(70.0 + 20.0 * ratio);
    }

    private static SKEncodedOrigin ReadOrientation(string filePath)
    {
        using var codec = SKCodec.Create(filePath);
        return codec?.EncodedOrigin ?? SKEncodedOrigin.TopLeft;
    }

    private static SKBitmap LoadAndOrient(string filePath, SKEncodedOrigin origin)
    {
        var bitmap = SKBitmap.Decode(filePath)
            ?? throw new InvalidOperationException("이미지를 디코딩할 수 없습니다.");

        if (origin == SKEncodedOrigin.TopLeft)
            return bitmap;

        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                                 or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int w = swap ? bitmap.Height : bitmap.Width;
        int h = swap ? bitmap.Width : bitmap.Height;

        var result = new SKBitmap(w, h);
        using var canvas = new SKCanvas(result);

        canvas.Translate(w / 2f, h / 2f);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.RotateDegrees(270);
                break;
        }

        canvas.DrawBitmap(bitmap, -bitmap.Width / 2f, -bitmap.Height / 2f);
        bitmap.Dispose();
        return result;
    }

    private static void GenerateThumbnail(string sourcePath, SKEncodedOrigin origin)
    {
        var thumbPath = Path.ChangeExtension(sourcePath, ".thm.jpg");
        if (File.Exists(thumbPath))
            throw new IOException($"파일이 이미 존재합니다: {Path.GetFileName(thumbPath)}");

        using var original = LoadAndOrient(sourcePath, origin);

        var minDim = Math.Min(original.Width, original.Height);
        var cropX = (original.Width - minDim) / 2;
        var cropY = (original.Height - minDim) / 2;

        using var resized = new SKBitmap(ThumbnailSize, ThumbnailSize);
        using (var canvas = new SKCanvas(resized))
        {
            var srcRect = new SKRect(cropX, cropY, cropX + minDim, cropY + minDim);
            var dstRect = new SKRect(0, 0, ThumbnailSize, ThumbnailSize);
            using var paint = new SKPaint();
            canvas.DrawBitmap(original, srcRect, dstRect, paint);
        }

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

        using var stream = File.Create(thumbPath);
        data.SaveTo(stream);
    }

    private static void GenerateWebp(string sourcePath, int quality, SKEncodedOrigin origin)
    {
        var webpPath = Path.ChangeExtension(sourcePath, ".webp.jpg");
        if (File.Exists(webpPath))
            throw new IOException($"파일이 이미 존재합니다: {Path.GetFileName(webpPath)}");

        using var original = LoadAndOrient(sourcePath, origin);

        using var image = SKImage.FromBitmap(original);
        using var data = image.Encode(SKEncodedImageFormat.Webp, quality);

        using var stream = File.Create(webpPath);
        data.SaveTo(stream);
    }
}
