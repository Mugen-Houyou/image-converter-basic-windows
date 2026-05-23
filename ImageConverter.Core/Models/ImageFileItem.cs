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
