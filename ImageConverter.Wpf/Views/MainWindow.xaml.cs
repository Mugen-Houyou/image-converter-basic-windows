using System.Windows;
using System.Windows.Input;
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
}
