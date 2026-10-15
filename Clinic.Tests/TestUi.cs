using Clinic.ViewModels;
internal static class TestUi
{
    public static async Task SignOutAsync(MainViewModel vm)
    {
        var work = vm.SignOutCommand.ExecuteAsync(null);
        if (vm.IsExitConfirmOpen) vm.ConfirmExitCommand.Execute(null);
        await work.WaitAsync(TimeSpan.FromSeconds(30));
    }
}
