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
    // r = 실제 bpp ÷ 전형적 bpp. 전형보다 작으면 작은 만큼 올리고(RaiseStart부터), 내리는 것은 아주 복잡한 이미지(LowerStart부터)만 한다.
    // 한계 두 개는 실제 이미지를 눈으로 비교해 정했다: 올림은 좋아지는 것이 보이는 데까지, 내림은 차이가 안 보이는 데까지.
    // 아래 값은 모두 보정 정도(strength)가 1일 때의 것이다.
    private const double RaiseStart = 1.0, RaiseFull = 0.67;    // r이 이 아래로 가면 올리기 시작 / 한계까지 올림
    private const double LowerStart = 1.75, LowerFull = 2.0;    // r이 이 위로 가면 내리기 시작 / 한계까지 내림
    private const double RaiseSizeLimit = 1.7;      // 올림 한계: 크기가 1.7배가 되는 퀄리티
    private const int MaxQuality = 95;              // 단 95를 넘기지 않는다 (100은 무손실로 바뀌어 크기가 몇 배로 뛴다)
    private const double LowerScaleLimit = 0.875;   // 내림 한계: 인코더 내부 척도의 0.875배 (75 위에서 약 -5, 아래에서 약 -9)

    // 보정 정도는 두 가지를 함께 움직인다.
    //  - 얼마나 멀리 가는가: 올림의 크기 목표와 내림의 척도 배율에 그대로 곱한다 (ln 기준). 1을 넘기면 눈으로 확인한 한계도 넘어간다.
    //  - 얼마나 선뜻 올리는가: 올림 구간 전체가 r이 큰 쪽으로 옮겨 간다. 정도가 1 늘 때마다 두 경계에 이 값을 곱한다
    //    (0이면 0.8부터, 1이면 1.0부터, 2면 1.25부터 올린다). 내리는 구간은 옮기지 않는다.
    private const double RaiseEagerness = 1.25;

    // 보정을 지원하는 출력 포맷. 슬라이더를 켤지(ViewModel)와 실제로 적용할지(변환)가 이 한 곳을 같이 본다.
    public static bool Supports(OutputFormat format) => format == OutputFormat.WebP;

    /// autoQuality로 인코딩한 결과의 크기(encodedBytes)를 보고 보정한 퀄리티를 돌려준다. 고칠 것이 없으면 autoQuality 그대로.
    /// strength: 보정 정도(0~2, 1이 기본). sizeLimit: 올린 결과가 넘으면 안 되는 크기(원본 파일 크기 등).
    public static int Correct(int autoQuality, long pixels, long encodedBytes, double strength, long sizeLimit)
    {
        if (strength <= 0 || encodedBytes <= 0 || pixels < MinPixels || pixels > MaxPixels)
            return autoQuality;

        double lnTypicalBpp = LnBppAt2MP + ResolutionSlope * Math.Log(pixels / 1e6 / 2) + LnSizeAt(autoQuality);
        double lnR = Math.Log(encodedBytes * 8.0 / pixels) - lnTypicalBpp;

        double lnShift = (strength - 1) * Math.Log(RaiseEagerness);
        double lnRaiseStart = Math.Log(RaiseStart) + lnShift, lnRaiseFull = Math.Log(RaiseFull) + lnShift;

        if (lnR < lnRaiseStart)
        {
            // 잘 압축되는 이미지: 크기 목표를 키우고, 그 크기가 되는 퀄리티를 K에서 찾는다
            double lnGrow = strength * Ramp(lnR, lnRaiseStart, lnRaiseFull) * Math.Log(RaiseSizeLimit);
            double room = Math.Log((double)sizeLimit / encodedBytes);   // 한도까지 남은 여유
            if (room <= 0) return autoQuality;

            double lnSize = LnSizeAt(autoQuality);
            int raised = (int)Math.Round(QualityAtSize(lnSize + lnGrow));
            // 예상 크기가 한도 안에 드는 가장 높은 퀄리티. 반올림으로 올라가면 한도를 넘으므로 내림한다
            int ceiling = (int)Math.Floor(QualityAtSize(lnSize + room));
            return Math.Max(autoQuality, Math.Min(Math.Min(MaxQuality, ceiling), raised));
        }

        if (lnR > Math.Log(LowerStart))
        {
            // 아주 복잡한 이미지: 인코더 내부 척도를 줄인다. 크기가 아니라 이 척도로 재야 해상도와 무관하게 같은 폭이 된다
            double scale = Math.Exp(
                strength * Ramp(lnR, Math.Log(LowerStart), Math.Log(LowerFull)) * Math.Log(LowerScaleLimit));
            int lowered = (int)Math.Round(FromEncoderScale(ToEncoderScale(autoQuality) * scale));
            return Math.Min(autoQuality, lowered);
        }

        return autoQuality;
    }

    /// 올린 결과가 한도를 넘었을 때 다시 잡을 퀄리티. 실제로 인코딩해 잰 두 크기(lowQuality→lowBytes, highQuality→highBytes)로
    /// 이 이미지가 전형(K)보다 얼마나 가파르게 커지는지 구해, sizeLimit 안에 드는 가장 높은 퀄리티를 돌려준다.
    /// 올릴 여지가 없으면 lowQuality 그대로.
    public static int FitWithin(int lowQuality, long lowBytes, int highQuality, long highBytes, long sizeLimit)
    {
        if (lowBytes >= sizeLimit || highBytes <= lowBytes || highQuality <= lowQuality) return lowQuality;

        double lnLow = LnSizeAt(lowQuality);
        double steepness = Math.Log((double)highBytes / lowBytes) / (LnSizeAt(highQuality) - lnLow);
        double quality = QualityAtSize(lnLow + Math.Log((double)sizeLimit / lowBytes) / steepness);
        return Math.Clamp((int)Math.Floor(quality), lowQuality, highQuality - 1);
    }

    // r이 start에서 full로 가는 동안 0→1 (ln r에 대해 직선). full을 지나면 1에 머문다.
    private static double Ramp(double lnR, double lnStart, double lnFull) =>
        Math.Clamp((lnR - lnStart) / (lnFull - lnStart), 0, 1);

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
