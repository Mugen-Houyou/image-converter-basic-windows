namespace ImageConverter.Core;

/// <summary>
/// 바이트 수 → 표시 문자열. 로그와 목록 '용량' 열이 같은 표기를 쓰도록 여기 하나로 모은다.
/// 1MB 미만은 KB 정수("278KB"), 이상은 MB 소수 한 자리("1.5MB"). 음수는 알 수 없음("—").
/// </summary>
public static class FileSize
{
    public static string Format(long bytes) => bytes < 0
        ? "—"
        : bytes < 1024L * 1024
            ? $"{bytes / 1024.0:F0}KB"
            : $"{bytes / (1024.0 * 1024):F1}MB";
}
