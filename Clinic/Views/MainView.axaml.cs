using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Clinic.ViewModels;

namespace Clinic.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("LoginInput")?.Focus());
        KeyDown += async (_, e) =>
        {
            if(e.Key != Key.Escape || DataContext is not MainViewModel model) return;
            if(model.IsRegistrationDiscardOpen) model.CancelRegistrationDiscardCommand.Execute(null);
            else if(model.IsRegistrationOpen) await model.CloseRegistrationCommand.ExecuteAsync(null);
            else return;
            e.Handled = true;
        };
    }
}
