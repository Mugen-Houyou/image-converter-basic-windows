using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using ImageConverter.Core.Models;
using ImageConverter.Core.Services;

namespace ImageConverter.Core.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private string _logText = string.Empty;
    private bool _isProcessing;
    private int _webpQuality = 90;
    private bool _isWebpQualityAuto = true;
    private bool _removeExif = true;
    private bool _isTargetSizeEnabled;
    private int _targetSizeKb = 300;

    public MainViewModel()
    {
        Files = new ObservableCollection<ImageFileItem>();

        AddFilesCommand = new DelegateCommand(_ => RequestAddFiles?.Invoke(), _ => !_isProcessing);
        StartConversionCommand = new DelegateCommand(_ => _ = StartConversionAsync(), _ => CanStartConversion());
        ClearFilesCommand = new DelegateCommand(_ => ClearFiles(), _ => Files.Count > 0 && !_isProcessing);
        ClearLogCommand = new DelegateCommand(_ => { LogText = string.Empty; });

        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasFiles));
            StartConversionCommand.RaiseCanExecuteChanged();
            ClearFilesCommand.RaiseCanExecuteChanged();
        };
    }

    // ── Properties ──

    public ObservableCollection<ImageFileItem> Files { get; }

    public string LogText
    {
        get => _logText;
        set { _logText = value; OnPropertyChanged(); }
    }

    public bool IsEmpty => Files.Count == 0;
    public bool HasFiles => Files.Count > 0;

    private const double DimmedOpacity = 0.4;

    public int WebpQuality
    {
        get => _webpQuality;
        set
        {
            if (_webpQuality == value) return;
            _webpQuality = value;
            OnPropertyChanged();
            // 유저가 슬라이더를 만지면(=Value 변경) 자동으로 수동 모드로 전환
            if (IsWebpQualityAuto)
                IsWebpQualityAuto = false;  // 내부에서 WebpQualityText, SliderOpacity 갱신됨
            else
                OnPropertyChanged(nameof(QualityText));
        }
    }

    public bool IsWebpQualityAuto
    {
        get => _isWebpQualityAuto;
        set
        {
            _isWebpQualityAuto = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(QualityText));
            OnPropertyChanged(nameof(SliderOpacity));
        }
    }

    public double SliderOpacity => IsWebpQualityAuto ? DimmedOpacity : 1.0;

    public string QualityText => IsWebpQualityAuto ? "품질: Auto" : $"품질: {WebpQuality}";

    public string[] OutputFormatNames { get; } = { "WEBP", "AVIF" };

    public string SelectedOutputFormatName
    {
        get => SelectedOutputFormat == OutputFormat.Avif ? "AVIF" : "WEBP";
        set
        {
            SelectedOutputFormat = value == "AVIF" ? OutputFormat.Avif : OutputFormat.WebP;
            OnPropertyChanged();
        }
    }

    public bool RemoveExif
    {
        get => _removeExif;
        set { _removeExif = value; OnPropertyChanged(); }
    }

    public bool IsTargetSizeEnabled
    {
        get => _isTargetSizeEnabled;
        set { _isTargetSizeEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(TargetSizeOpacity)); }
    }

    public int TargetSizeKb
    {
        get => _targetSizeKb;
        set { _targetSizeKb = value; OnPropertyChanged(); }
    }

    // 타깃 용량 비활성 시 KB 입력란을 흐리게 (슬라이더 SliderOpacity와 동일 패턴)
    public double TargetSizeOpacity => IsTargetSizeEnabled ? 1.0 : DimmedOpacity;

    private OutputFormat _selectedOutputFormat = OutputFormat.WebP;

    public OutputFormat SelectedOutputFormat
    {
        get => _selectedOutputFormat;
        set { _selectedOutputFormat = value; OnPropertyChanged(); }
    }

    // ── Commands ──

    public DelegateCommand AddFilesCommand { get; }
    public DelegateCommand StartConversionCommand { get; }
    public DelegateCommand ClearFilesCommand { get; }
    public DelegateCommand ClearLogCommand { get; }

    // ── Callback for View ──

    public event Action? RequestAddFiles;

    // ── Public methods ──

    public void AddFiles(IEnumerable<string> filePaths)
    {
        var added = 0;
        foreach (var path in filePaths)
        {
            if (!ImageConversionService.IsSupportedFile(path)) continue;
            if (Files.Any(f => string.Equals(f.FilePath, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            Files.Add(new ImageFileItem
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                Status = ConversionStatus.Waiting,
                StatusText = "대기중"
            });
            added++;
        }

        if (added > 0)
            AppendLog($"파일 {added}개 추가됨");
    }

    public void ClearFiles()
    {
        Files.Clear();
    }

    // ── Internal logic ──

    private bool CanStartConversion()
    {
        return Files.Count > 0 && !_isProcessing && Files.Any(f => f.Status == ConversionStatus.Waiting);
    }

    private async Task StartConversionAsync()
    {
        _isProcessing = true;
        AddFilesCommand.RaiseCanExecuteChanged();
        StartConversionCommand.RaiseCanExecuteChanged();
        ClearFilesCommand.RaiseCanExecuteChanged();

        try
        {
            foreach (var file in Files.Where(f => f.Status == ConversionStatus.Waiting).ToList())
            {
                file.Status = ConversionStatus.Processing;

                int quality = IsWebpQualityAuto
                    ? ImageConversionService.CalculateAutoQuality(file.FilePath, SelectedOutputFormat)
                    : WebpQuality;

                long? targetBytes = IsTargetSizeEnabled ? TargetSizeKb * 1024L : null;

                AppendLog($"변환 시작: {file.FileName} (퀄리티 {quality})");

                try
                {
                    var (success, error, note) = await ImageConversionService.ConvertAsync(
                        file.FilePath, quality, RemoveExif, SelectedOutputFormat, targetBytes);

                    if (success)
                    {
                        file.Status = ConversionStatus.Completed;
                        file.StatusText = "완료";
                        AppendLog(note.Length > 0
                            ? $"완료: {file.FileName} ({note})"
                            : $"완료: {file.FileName}");
                    }
                    else
                    {
                        file.Status = ConversionStatus.Failed;
                        file.StatusText = error.Length > 0 ? $"실패: {error}" : "실패";
                        AppendLog($"실패: {file.FileName} - {error}");
                    }
                }
                catch (Exception ex)
                {
                    file.Status = ConversionStatus.Failed;
                    file.StatusText = $"실패: {ex.Message}";
                    AppendLog($"실패: {file.FileName} - {ex.Message}");
                }
            }
        }
        finally
        {
            _isProcessing = false;
            AddFilesCommand.RaiseCanExecuteChanged();
            StartConversionCommand.RaiseCanExecuteChanged();
            ClearFilesCommand.RaiseCanExecuteChanged();
        }
    }

    private void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        UiDispatch.InvokeAsync?.Invoke(() =>
        {
            LogText += $"[{timestamp}] {message}\n";
        });
    }

    // ── INotifyPropertyChanged ──

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
