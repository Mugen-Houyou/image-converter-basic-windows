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

    public static Task<(bool success, string error, string note)> ConvertAsync(
        string filePath, int quality, bool removeExif,
        OutputFormat outputFormat, long? targetSizeBytes = null, CancellationToken ct = default)
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

                var note = GenerateOutput(filePath, quality, outputFormat, outputOrigin, targetSizeBytes);

                return (true, "", note);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (false, ex.Message, "");
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

    // ── 출력 생성 (타깃 용량 탐색 포함) ──

    private const int MinTargetDim = 200;        // 타깃 탐색 시 짧은 변 최소 px
    private const int MaxTargetPasses = 3;        // 인코딩 반복 상한
    private const double TargetTolerance = 0.12;  // ±12% 이내면 보정 중단 (소프트 타깃)

    private static string GenerateOutput(
        string sourcePath, int quality, OutputFormat format,
        SKEncodedOrigin origin, long? targetSizeBytes)
    {
        var ext = format == OutputFormat.Avif ? ".avif.jpg" : ".webp.jpg";
        var outPath = Path.ChangeExtension(sourcePath, ext);
        if (File.Exists(outPath))
            throw new IOException($"파일이 이미 존재합니다: {Path.GetFileName(outPath)}");

        using var original = LoadAndOrient(sourcePath, origin);
        var (bytes, width, height) = EncodeToTarget(original, format, quality, targetSizeBytes);
        File.WriteAllBytes(outPath, bytes);

        // 타깃 모드일 때만 결과 해상도/용량 요약을 반환 (로그 표시용)
        return targetSizeBytes is null ? "" : $"{width}×{height}, {FormatKb(bytes.LongLength)}";
    }

    // 파일 크기는 해상도로부터 해석적으로 계산 불가 → 인코딩→측정→스케일 보정으로 근접시킨다.
    // 퀄리티는 고정하고 해상도만 조정한다. 업스케일은 하지 않으며, 타깃은 소프트(부근이면 OK).
    private static (byte[] bytes, int width, int height) EncodeToTarget(
        SKBitmap full, OutputFormat format, int quality, long? targetSizeBytes)
    {
        if (targetSizeBytes is not long target)
            return (Encode(full, format, quality), full.Width, full.Height);

        var best = Encode(full, format, quality);
        int bestW = full.Width, bestH = full.Height;

        // 이미 타깃 이하면 원본 해상도 유지 (업스케일 금지)
        if (best.LongLength <= target)
            return (best, bestW, bestH);

        double minScale = (double)MinTargetDim / Math.Min(full.Width, full.Height);
        double scale = Math.Sqrt((double)target / best.LongLength);

        for (int pass = 0; pass < MaxTargetPasses; pass++)
        {
            if (scale < minScale) scale = minScale;
            if (scale >= 1.0) break;

            using var resized = ResizeBitmap(full, scale);
            var bytes = Encode(resized, format, quality);

            // 타깃에 더 가까우면 채택
            if (Math.Abs(bytes.LongLength - target) < Math.Abs(best.LongLength - target))
            {
                best = bytes; bestW = resized.Width; bestH = resized.Height;
            }

            double err = (double)(bytes.LongLength - target) / target;
            if (Math.Abs(err) <= TargetTolerance) break;            // 부근이면 종료
            if (scale <= minScale) break;                           // floor 도달, 더 못 줄임
            // 국소 고정점 보정. log-log 전역 지수 추정도 시도했으나 실측상 더 낫지 않아(곡선이
            // 깨끗한 거듭제곱이 아님) 단순·견고한 이 방식을 유지. scale *= √(타깃/실제크기).
            scale *= Math.Sqrt((double)target / bytes.LongLength);
        }

        return (best, bestW, bestH);
    }

    private static byte[] Encode(SKBitmap bmp, OutputFormat format, int quality) =>
        format == OutputFormat.Avif ? EncodeAvif(bmp, quality) : EncodeWebp(bmp, quality);

    private static byte[] EncodeWebp(SKBitmap bmp, int quality)
    {
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Webp, quality);
        return data.ToArray();
    }

    private static byte[] EncodeAvif(SKBitmap bmp, int quality)
    {
        // BGRA8888로 정규화 — ColorType은 플랫폼 의존, CopyTo로 packed BGRA 보장 (stride padding 제거)
        using var bgra = new SKBitmap(new SKImageInfo(
            bmp.Width, bmp.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        bmp.CopyTo(bgra, SKColorType.Bgra8888);

        var settings = new PixelReadSettings(
            (uint)bgra.Width, (uint)bgra.Height, StorageType.Char, "BGRA");

        using var image = new MagickImage();
        image.ReadPixels(bgra.Bytes, settings);
        image.Format = MagickFormat.Avif;
        image.Quality = (uint)quality;
        return image.ToByteArray();
    }

    private static SKBitmap ResizeBitmap(SKBitmap src, double scale)
    {
        int w = Math.Max(1, (int)Math.Round(src.Width * scale));
        int h = Math.Max(1, (int)Math.Round(src.Height * scale));

        var dst = new SKBitmap(new SKImageInfo(w, h, src.ColorType, src.AlphaType));
        using var canvas = new SKCanvas(dst);
        using var image = SKImage.FromBitmap(src);
        // 썸네일과 동일한 mipmap 트라이리니어로 alias 없는 다운스케일
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        canvas.DrawImage(image, new SKRect(0, 0, src.Width, src.Height),
                                new SKRect(0, 0, w, h), sampling);
        return dst;
    }

    private static string FormatKb(long bytes) => $"{bytes / 1024.0:F0}KB";
}
