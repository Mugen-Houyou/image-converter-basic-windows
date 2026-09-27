using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ImageConverter.Core.Models;

public enum ConversionStatus
{
    Waiting,
    Processing,
    Completed,
    Failed
}

public class ImageFileItem : INotifyPropertyChanged
{
    private string _filePath = string.Empty;
    private string _fileName = string.Empty;
    private long _fileSizeBytes = -1;
    private int _width;
    private int _height;
    private string _format = string.Empty;
    private ConversionStatus _status = ConversionStatus.Waiting;
    private string _statusText = "대기중";

    public string FilePath
    {
        get => _filePath;
        set { _filePath = value; OnPropertyChanged(); }
    }

    public string FileName
    {
        get => _fileName;
        set { _fileName = value; OnPropertyChanged(); }
    }

    // 원본 파일 크기(바이트). 음수면 알 수 없음(읽기 실패 등) → "—"로 표시.
    public long FileSizeBytes
    {
        get => _fileSizeBytes;
        set
        {
            _fileSizeBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FileSizeText));
            OnPropertyChanged(nameof(PreviewInfoText));
        }
    }

    // 목록 '용량' 열 표시용. 로그와 같은 포맷터(FileSize.Format)를 쓴다.
    public string FileSizeText => FileSize.Format(_fileSizeBytes);

    // 원본 해상도(EXIF 회전 반영)·포맷. 추가 시점에 헤더만 읽어 채운다(ImageConversionService.ReadImageInfo).
    // 0 / 빈 문자열이면 알 수 없음.
    public int Width
    {
        get => _width;
        set { _width = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewInfoText)); }
    }

    public int Height
    {
        get => _height;
        set { _height = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewInfoText)); }
    }

    public string Format
    {
        get => _format;
        set { _format = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewInfoText)); }
    }

    // 미리보기 툴팁 하단 한 줄: "1920x1080, 123KB, JPEG"
    public string PreviewInfoText
    {
        get
        {
            var dims = _width > 0 && _height > 0 ? $"{_width}x{_height}" : "—";
            var fmt = _format.Length > 0 ? _format : "—";
            return $"{dims}, {FileSizeText}, {fmt}";
        }
    }

    public ConversionStatus Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsProcessing));
            OnPropertyChanged(nameof(IsCompleted));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(StatusIcon));
        }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public bool IsProcessing => Status == ConversionStatus.Processing;
    public bool IsCompleted => Status == ConversionStatus.Completed;
    public bool IsFailed => Status == ConversionStatus.Failed;

    public string StatusIcon => Status switch
    {
        ConversionStatus.Waiting => "○",
        ConversionStatus.Processing => "◎",
        ConversionStatus.Completed => "✓",
        ConversionStatus.Failed => "✗",
        _ => ""
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
