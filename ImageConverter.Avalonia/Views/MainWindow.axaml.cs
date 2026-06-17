using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ImageConverter.Core.ViewModels;

namespace ImageConverter.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? _previousVm;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_previousVm is not null)
            _previousVm.RequestAddFiles -= OpenFileDialog;

        if (DataContext is MainViewModel vm)
        {
            _previousVm = vm;
            vm.RequestAddFiles += OpenFileDialog;
        }
    }

    private async void OpenFileDialog()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "이미지 파일 선택",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("이미지 파일")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif" }
                }
            }
        });

        if (files.Count > 0 && DataContext is MainViewModel vm)
        {
            vm.AddFiles(files.Select(f => f.TryGetLocalPath()!).Where(p => p is not null));
        }
    }

    private void FileList_DragEnter(object? sender, DragEventArgs e) => UpdateDragFeedback(e);

    private void FileList_DragOver(object? sender, DragEventArgs e) => UpdateDragFeedback(e);

    // Win9x target feedback: highlight the drop zone with the system selection color
    // (navy #000080) while a valid file drag hovers. DragOver re-asserts on every move so
    // entering/leaving child list items can't flicker the highlight off.
    private void UpdateDragFeedback(DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            DropHighlight.IsVisible = true;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void FileList_DragLeave(object? sender, DragEventArgs e)
    {
        DropHighlight.IsVisible = false;
    }

    private void FileList_Drop(object? sender, DragEventArgs e)
    {
        DropHighlight.IsVisible = false;
        var files = e.Data.GetFiles();
        if (files != null && DataContext is MainViewModel vm)
        {
            var paths = files
                .Select(f => f.TryGetLocalPath())
                .Where(p => p is not null)
                .Cast<string>();
            vm.AddFiles(paths);
        }
    }

    private void AboutButton_Click(object? sender, RoutedEventArgs e)
    {
        var about = new AboutWindow();
        about.ShowDialog(this);
    }

    private void WebpArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(sender as Control).Properties.IsRightButtonPressed
            && DataContext is MainViewModel vm)
        {
            vm.IsWebpQualityAuto = !vm.IsWebpQualityAuto;
            e.Handled = true;
        }
    }
}
