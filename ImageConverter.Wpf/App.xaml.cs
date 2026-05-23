using System.Windows;
using ImageConverter.Core;
using ImageConverter.Core.ViewModels;

namespace ImageConverter.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        UiDispatch.InvokeAsync = a => Application.Current.Dispatcher.Invoke(a);
        var mainWindow = new Views.MainWindow();
        mainWindow.DataContext = new MainViewModel();
        mainWindow.Show();
    }
}
