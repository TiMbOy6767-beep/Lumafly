using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Lumafly.Util;
using Lumafly.ViewModels;

namespace Lumafly.Views.Windows;

public partial class ReadmePopup : Window
{
    public ReadmePopup()
    {
        InitializeComponent();

        var mainWindow = AvaloniaUtils.GetMainWindow();
        Width = mainWindow.Width;
        Height = mainWindow.Height;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (DataContext != null)
            MarkdownViewer.Plugins = ((ReadmePopupViewModel)DataContext!).MarkdownPlugins;

        base.OnDataContextChanged(e);
    }

    private void Close(object? sender, RoutedEventArgs e) => Close();
}