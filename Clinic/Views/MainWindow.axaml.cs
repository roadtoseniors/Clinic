using System;
using Avalonia.Controls;
using Clinic.ViewModels;

namespace Clinic.Views;

public partial class MainWindow : Window
{
    private bool _closeApproved;
    public MainWindow()
    {
        InitializeComponent();
        Closing += ProtectUnsavedChanges;
    }
    private async void ProtectUnsavedChanges(object? sender, WindowClosingEventArgs e)
    {
        if (_closeApproved || DataContext is not MainViewModel model) return;
        e.Cancel = true;
        if (!await model.ConfirmWindowCloseAsync()) return;
        _closeApproved = true;
        Close();
    }
}
