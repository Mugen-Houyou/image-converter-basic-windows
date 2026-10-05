using ImageConverter.Core.Models;

namespace ImageConverter.Core;

/// <summary>
/// Auto 퀄리티를 이미지 내용에 맞춰 보정한다. Auto 퀄리티는 해상도만 보고 정하므로, 그 퀄리티로 인코딩한 결과가
/// 같은 해상도의 전형적인 크기보다 훨씬 작으면(잘 압축됨) 퀄리티를 올리고, 훨씬 크면(아주 복잡함) 내린다.
/// </summary>
public static class AutoQualityCorrection
{
    // ── 전형적 크기 ──
    // 전형적 bpp(픽셀당 비트) = exp(LnBppAt2MP) × (MP/2)^ResolutionSlope × K(퀄리티)
    // 실제 이미지 654장(0.3MP 이상)을 이 앱의 인코더로 Auto 퀄리티에서 인코딩해 맞춘 값이다.
    private const double LnBppAt2MP = -0.8899;        // 2MP, 퀄리티 75에서의 ln(bpp)
    private const double ResolutionSlope = -0.3363;   // 해상도가 2배가 되면 bpp는 약 21% 줄어든다

    // K(퀄리티): 같은 이미지를 퀄리티 75로 인코딩한 크기에 대한 배율의 ln. 격자 사이는 직선으로 잇는다.
    // 75를 지나며 기울기가 3배쯤 달라지는 것은 libwebp가 75 위와 아래에서 퀄리티를 다른 폭으로 쓰기 때문이다.
    private static readonly double[] KQuality = { 70, 75, 80, 85, 90, 92, 94, 96, 98 };
    private static readonly double[] KLnSize = { -0.0602, 0, 0.1754, 0.3654, 0.5698, 0.6815, 0.8158, 0.9591, 1.0890 };

    private const long MinPixels = 100_000, MaxPixels = 50_000_000;   // 위 식을 믿을 수 있는 해상도 범위

    // ── 보정 규칙 ──
    // r = 실제 bpp ÷ 전형적 bpp. 전형에 가까운 이미지(RaiseStart~LowerStart)는 건드리지 않고 양 끝만 고친다.
    // 한계 두 개는 실제 이미지를 눈으로 비교해 정했다: 올림은 좋아지는 것이 보이는 데까지, 내림은 차이가 안 보이는 데까지.
    private const double RaiseStart = 0.8, RaiseFull = 0.67;    // r이 이 아래로 가면 올리기 시작 / 한계까지 올림
    private const double LowerStart = 1.75, LowerFull = 2.0;    // r이 이 위로 가면 내리기 시작 / 한계까지 내림
    private const double RaiseSizeLimit = 1.7;      // 올림 한계: 크기가 1.7배가 되는 퀄리티
    private const int MaxQuality = 95;              // 단 95를 넘기지 않는다 (100은 무손실로 바뀌어 크기가 몇 배로 뛴다)
    private const double LowerScaleLimit = 0.875;   // 내림 한계: 인코더 내부 척도의 0.875배 (75 위에서 약 -5, 아래에서 약 -9)

    // 보정을 지원하는 출력 포맷. 슬라이더를 켤지(ViewModel)와 실제로 적용할지(변환)가 이 한 곳을 같이 본다.
    public static bool Supports(OutputFormat format) => format == OutputFormat.WebP;

    /// autoQuality로 인코딩한 결과의 크기(encodedBytes)를 보고 보정한 퀄리티를 돌려준다. 고칠 것이 없으면 autoQuality 그대로.
    /// strength: 보정을 적용하는 정도(0~1). sizeLimit: 올린 결과가 넘으면 안 되는 크기(원본 파일 크기 등).
    public static int Correct(int autoQuality, long pixels, long encodedBytes, double strength, long sizeLimit)
    {
        if (strength <= 0 || encodedBytes <= 0 || pixels < MinPixels || pixels > MaxPixels)
            return autoQuality;

        double lnTypicalBpp = LnBppAt2MP + ResolutionSlope * Math.Log(pixels / 1e6 / 2) + LnSizeAt(autoQuality);
        double lnR = Math.Log(encodedBytes * 8.0 / pixels) - lnTypicalBpp;

        if (lnR < Math.Log(RaiseStart))
        {
            // 잘 압축되는 이미지: 크기 목표를 키우고, 그 크기가 되는 퀄리티를 K에서 찾는다
            double lnGrow = strength * Ramp(lnR, RaiseStart, RaiseFull) * Math.Log(RaiseSizeLimit);
            double room = Math.Log((double)sizeLimit / encodedBytes);   // 한도까지 남은 여유
            bool limited = room < lnGrow;
            if (limited) lnGrow = room;
            if (lnGrow <= 0) return autoQuality;

            double quality = QualityAtSize(LnSizeAt(autoQuality) + lnGrow);
            // 한도에 맞춰 줄인 경우에는 반올림으로 올라가면 한도를 넘으므로 내림한다
            int raised = (int)(limited ? Math.Floor(quality) : Math.Round(quality));
            return Math.Max(autoQuality, Math.Min(MaxQuality, raised));
        }

        if (lnR > Math.Log(LowerStart))
        {
            // 아주 복잡한 이미지: 인코더 내부 척도를 줄인다. 크기가 아니라 이 척도로 재야 해상도와 무관하게 같은 폭이 된다
            double scale = Math.Exp(strength * Ramp(lnR, LowerStart, LowerFull) * Math.Log(LowerScaleLimit));
            int lowered = (int)Math.Round(FromEncoderScale(ToEncoderScale(autoQuality) * scale));
            return Math.Min(autoQuality, lowered);
        }

        return autoQuality;
    }

    // r이 start에서 full로 가는 동안 0→1 (ln r에 대해 직선). full을 지나면 1에 머문다.
    private static double Ramp(double lnR, double start, double full) =>
        Math.Clamp((lnR - Math.Log(start)) / (Math.Log(full) - Math.Log(start)), 0, 1);

    // libwebp가 퀄리티를 받아 처음 바꾸는 값. 75 아래에서는 퀄리티 1의 효과가 위의 3분의 1이다.
    private static double ToEncoderScale(double quality) => quality < 75 ? quality / 150 : quality / 50 - 1;

    private static double FromEncoderScale(double scale) => scale < 0.5 ? scale * 150 : (scale + 1) * 50;

    private static double LnSizeAt(double quality) => Interpolate(KQuality, KLnSize, quality);

    private static double QualityAtSize(double lnSize) => Interpolate(KLnSize, KQuality, lnSize);

    // 표의 격자 사이를 직선으로 잇는다. 표 밖은 끝 값에 머문다.
    private static double Interpolate(double[] xs, double[] ys, double x)
    {
        if (x <= xs[0]) return ys[0];
        if (x >= xs[^1]) return ys[^1];

        int i = 1;
        while (x > xs[i]) i++;
        return ys[i - 1] + (ys[i] - ys[i - 1]) * (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
    }
}
