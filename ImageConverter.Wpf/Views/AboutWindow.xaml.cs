using System.Windows;

namespace ImageConverter.Wpf.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        CurrentYear.Text = DateTime.Now.Year.ToString();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
