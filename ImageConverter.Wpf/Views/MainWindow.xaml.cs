using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ImageConverter.Core.Models;
using ImageConverter.Core.ViewModels;

namespace ImageConverter.Wpf.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
            oldVm.RequestAddFiles -= OpenFileDialog;

        if (e.NewValue is MainViewModel newVm)
            newVm.RequestAddFiles += OpenFileDialog;
    }

    private void OpenFileDialog()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "이미지 파일 선택",
            Filter = "이미지 파일|*.png;*.jpg;*.jpeg;*.bmp;*.gif|모든 파일|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog() == true && DataContext is MainViewModel vm)
        {
            vm.AddFiles(dlg.FileNames);
        }
    }

    private void FileListView_DragEnter(object sender, DragEventArgs e) => UpdateDragFeedback(e);

    private void FileListView_DragOver(object sender, DragEventArgs e) => UpdateDragFeedback(e);

    // Highlight the drop zone with the system selection color while a valid file drag hovers.
    // DragOver re-asserts on every move so entering/leaving child list items can't flicker it off.
    private void UpdateDragFeedback(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropHighlight.Visibility = Visibility.Visible;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void FileListView_DragLeave(object sender, DragEventArgs e)
    {
        DropHighlight.Visibility = Visibility.Collapsed;
    }

    private bool _logAutoScroll = true;

    // 로그 자동 스크롤(카톡/WhatsApp 방식): 맨 아래를 보고 있을 때만 새 로그를 따라 내려가고,
    // 위로 올려 과거 로그를 읽는 중이면 끌어내리지 않는다. ExtentHeightChange==0 이면 사용자 스크롤.
    private void LogTextBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange == 0)
            _logAutoScroll = e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 1;
        else if (_logAutoScroll)
            ((TextBox)sender).ScrollToEnd();
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private void WebpArea_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.IsWebpQualityAuto = !vm.IsWebpQualityAuto;
            e.Handled = true;
        }
    }

    private void FileListView_Drop(object sender, DragEventArgs e)
    {
        DropHighlight.Visibility = Visibility.Collapsed;
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && DataContext is MainViewModel vm)
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            vm.AddFiles(files);
        }
    }

    // Delete 키: 선택한 항목(들)을 목록에서 제거 (탐색기와 동일한 단축키).
    private void FileListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || DataContext is not MainViewModel vm) return;

        var selected = FileListView.SelectedItems.OfType<ImageFileItem>().ToList();
        if (selected.Count == 0) return;

        int anchor = selected.Min(item => vm.Files.IndexOf(item));
        vm.RemoveFiles(selected);
        SelectAndFocus(Math.Min(anchor, vm.Files.Count - 1));
        e.Handled = true;
    }

    // 탐색기처럼 삭제 후 같은 위치의 다음 항목을 선택하고 포커스를 옮긴다 (Delete 연타로 계속 지울 수 있게).
    private void SelectAndFocus(int index)
    {
        if (index < 0) return;
        FileListView.SelectedIndex = index;
        FileListView.UpdateLayout();
        if (FileListView.ItemContainerGenerator.ContainerFromIndex(index) is ListViewItem item)
            item.Focus();
    }
}
