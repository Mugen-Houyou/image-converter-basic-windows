using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ImageConverter.Avalonia.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        CopyrightText.Text = $"Copyright © Mugen-Houyou, 2026-{DateTime.Now.Year}. All rights reserved.";
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
