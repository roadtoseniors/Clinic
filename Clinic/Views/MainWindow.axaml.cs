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
        if (model.HasRegistrationChanges)
        {
            e.Cancel = true;
            if(model.IsBusy) return;
            if(!await model.ConfirmRegistrationDiscardAsync()) return;
        }
        else if (model.Workspace is { HasUnsavedChanges: true } workspace)
        {
            e.Cancel = true;
            if(workspace.IsBusy) return;
            if(!await workspace.ConfirmDiscardAsync()) return;
        }
        else return;
        _closeApproved = true;
        Close();
    }
}
