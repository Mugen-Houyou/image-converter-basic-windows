using System.IO;
using ImageMagick;
using ImageConverter.Core.Models;
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

    public static Task<(bool success, string error)> ConvertAsync(
        string filePath, int quality, bool removeExif,
        OutputFormat outputFormat, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var thumbnailOrigin = ReadOrientation(filePath);
                // WebP: 기존 동작 유지 (removeExif=true일 때만 orientation bake)
                // AVIF: 항상 bake (raw pixel 전달이라 EXIF 기록 불가)
                var outputOrigin = (outputFormat == OutputFormat.Avif || removeExif)
                    ? thumbnailOrigin
                    : SKEncodedOrigin.TopLeft;

                GenerateThumbnail(filePath, thumbnailOrigin);

                switch (outputFormat)
                {
                    case OutputFormat.Avif:
                        GenerateAvif(filePath, quality, outputOrigin);
                        break;
                    default:
                        GenerateWebp(filePath, quality, outputOrigin);
                        break;
                }

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

    public static int CalculateAutoQuality(string filePath, OutputFormat format)
    {
        using var codec = SKCodec.Create(filePath);

        // WebP: HD이하 90, 4K이상 70
        // AVIF: HD이하 75, 4K이상 50 (AV1의 더 높은 압축 효율 반영)
        int highQ = format == OutputFormat.Avif ? 75 : 90;
        int lowQ  = format == OutputFormat.Avif ? 55 : 70;

        if (codec is null) return highQ;

        long pixels = (long)codec.Info.Width * codec.Info.Height;

        if (pixels <= HdPixels) return highQ;
        if (pixels >= FourKPixels) return lowQ;

        double ratio = (double)(FourKPixels - pixels) / (FourKPixels - HdPixels);
        return (int)Math.Round(lowQ + (highQ - lowQ) * ratio);
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
            return; // 썸네일은 포맷 무관하게 동일 — 이미 있으면 건너뛰기

        using var original = LoadAndOrient(sourcePath, origin);

        var minDim = Math.Min(original.Width, original.Height);
        var cropX = (original.Width - minDim) / 2;
        var cropY = (original.Height - minDim) / 2;

        using var resized = new SKBitmap(ThumbnailSize, ThumbnailSize);
        using (var canvas = new SKCanvas(resized))
        using (var sourceImage = SKImage.FromBitmap(original))
        {
            var srcRect = new SKRect(cropX, cropY, cropX + minDim, cropY + minDim);
            var dstRect = new SKRect(0, 0, ThumbnailSize, ThumbnailSize);
            // 대배율 축소(예: 4000px→120px)에서는 nearest/bilinear/bicubic 모두 aliasing이 심하다.
            // mipmap 트라이리니어만이 출력 픽셀 footprint를 제대로 평균화해 alias 없는 썸네일을 만든다.
            // (DrawBitmap에는 SKSamplingOptions 오버로드가 없어 SKImage 경유로 DrawImage 사용)
            var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
            canvas.DrawImage(sourceImage, srcRect, dstRect, sampling);
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

    private static void GenerateAvif(string sourcePath, int quality, SKEncodedOrigin origin)
    {
        var avifPath = Path.ChangeExtension(sourcePath, ".avif.jpg");
        if (File.Exists(avifPath))
            throw new IOException($"파일이 이미 존재합니다: {Path.GetFileName(avifPath)}");

        using var original = LoadAndOrient(sourcePath, origin);

        // BGRA8888로 정규화 — LoadAndOrient의 반환 ColorType은 플랫폼 의존
        // CopyTo로 packed BGRA를 보장 (stride padding 제거)
        using var bgra = new SKBitmap(new SKImageInfo(
            original.Width, original.Height,
            SKColorType.Bgra8888, SKAlphaType.Unpremul));
        original.CopyTo(bgra, SKColorType.Bgra8888);

        var settings = new PixelReadSettings(
            (uint)bgra.Width, (uint)bgra.Height,
            StorageType.Char, "BGRA");

        using var image = new MagickImage();
        image.ReadPixels(bgra.Bytes, settings);
        image.Format = MagickFormat.Avif;
        image.Quality = (uint)quality;
        image.Write(avifPath);
    }
}
