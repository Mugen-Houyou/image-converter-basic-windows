using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ImageConverter.Core.Models;
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

    // 선택한 항목(들)을 목록에서 제거. Windows/Linux는 Delete, macOS는 ⌘⌫(Finder '휴지통으로 이동'과 동일).
    // Finder처럼 macOS에선 modifier 없는 Delete로는 지우지 않는다.
    private void FileList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsRemoveShortcut(e) || DataContext is not MainViewModel vm) return;

        var selected = FileListBox.SelectedItems?.OfType<ImageFileItem>().ToList();
        if (selected is null || selected.Count == 0) return;

        int anchor = selected.Min(item => vm.Files.IndexOf(item));
        vm.RemoveFiles(selected);
        SelectAndFocus(Math.Min(anchor, vm.Files.Count - 1));
        e.Handled = true;
    }

    private static bool IsRemoveShortcut(KeyEventArgs e) =>
        OperatingSystem.IsMacOS()
            ? e.Key == Key.Back && e.KeyModifiers.HasFlag(KeyModifiers.Meta)
            : e.Key == Key.Delete;

    // 탐색기/Finder처럼 삭제 후 같은 위치의 다음 항목을 선택하고 포커스를 옮긴다 (연타 삭제 가능).
    private void SelectAndFocus(int index)
    {
        if (index < 0) return;
        FileListBox.SelectedIndex = index;
        FileListBox.UpdateLayout();
        FileListBox.ContainerFromIndex(index)?.Focus();
    }

    private bool _logAutoScroll = true;

    // 로그 자동 스크롤(카톡/WhatsApp 방식): 맨 아래를 보고 있을 때만 새 로그를 따라 내려가고,
    // 위로 올려 과거 로그를 읽는 중이면 끌어내리지 않는다. ExtentDelta.Y==0 이면 사용자 스크롤.
    private void LogTextBox_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer sv) return;

        if (e.ExtentDelta.Y == 0)
            _logAutoScroll = sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - 1;
        else if (_logAutoScroll)
            sv.ScrollToEnd();
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
