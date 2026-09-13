namespace ImageConverter.Core;

/// <summary>
/// 선형 슬라이더 위치(0~100)를 인코더 퀄리티(0~100)로 거듭제곱 커브 매핑.
/// 지수 &lt; 1 이면 저품질 구간을 압축하고 고품질 구간을 넓혀,
/// 지각 차이가 큰 상단에서 미세 조정 해상도를 확보한다.
/// </summary>
public static class QualityCurve
{
    public const double QMin = 0;
    public const double QMax = 100;
    public const double Gamma = 0.4;

    /// 슬라이더 위치(0~100) → 퀄리티(0~100). round(QMax·(pos/QMax)^Gamma)
    public static int PositionToQuality(double position)
    {
        if (position <= QMin) return (int)QMin;   // 음수/0 방어 (Pow(음수,0.6)=NaN)
        if (position >= QMax) return (int)QMax;
        return (int)Math.Round(QMax * Math.Pow(position / QMax, Gamma));
    }

    /// 역함수: 퀄리티 → 그 퀄리티를 만드는 슬라이더 위치. 초기 기본값 설정용.
    public static double QualityToPosition(int quality)
    {
        if (quality <= (int)QMin) return QMin;
        if (quality >= (int)QMax) return QMax;
        return QMax * Math.Pow((double)quality / QMax, 1.0 / Gamma);
    }
}
