using Clinic.Demo;
using Clinic.ViewModels;
using Clinic.Workspace;
internal static class UiFlowTests
{
    public static async Task<int> RunAsync()
    {
        var checks=0;void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
        var vm=new MainViewModel(()=>new DemoGateway()){Login="sokolov",Password="demo123"};await vm.SignInCommand.ExecuteAsync(null);
        var exit=vm.SignOutCommand.ExecuteAsync(null);Check(vm.IsExitConfirmOpen&&!exit.IsCompleted,"Clean signout asks for confirmation");
        vm.CancelExitCommand.Execute(null);await exit;Check(vm.IsSignedIn,"Cancel signout keeps session");
        var close=vm.ConfirmWindowCloseAsync();Check(vm.IsExitConfirmOpen,"Window close asks for confirmation");vm.CancelExitCommand.Execute(null);Check(!await close,"Cancel window close keeps application");
        var ws=vm.Workspace!;await ws.ChangePasswordCommand.ExecuteAsync(null);ws.Fields.First(f=>f.Spec.Key=="new_password").Text="changed123";
        var original=ws.SelectedSection;var target=ws.Sections.First(s=>s.Key=="schedule");ws.SelectedSection=target;
        Check(ws.SelectedSection==original&&ws.IsActionConfirmOpen&&ws.IsEditorOpen,"Navigation protects dirty editor");
        ws.CancelActionCommand.Execute(null);await Task.Delay(20);Check(ws.SelectedSection==original&&ws.HasUnsavedChanges,"Cancel navigation preserves form");
        ws.SelectedSection=target;ws.ConfirmActionCommand.Execute(null);
        for(var i=0;i<100&&(ws.SelectedSection!=target||ws.IsEditorOpen);i++)await Task.Delay(10);
        Check(ws.SelectedSection==target&&!ws.IsEditorOpen,"Confirmed navigation discards form");
        await TestUi.SignOutAsync(vm);Check(vm.IsSignedOut,"Confirmed signout clears session");
        close=vm.ConfirmWindowCloseAsync();vm.ConfirmExitCommand.Execute(null);Check(await close,"Signed out window close can be confirmed");
        Check(InputRules.Error("phone","+7 (999) 123-45-67") is null,"Formatted phone accepted");
        Check(InputRules.Error("phone","invalid") is not null,"Invalid phone rejected");
        Check(InputRules.Error("snils","123-456-789 00") is null,"SNILS format accepted");
        Check(InputRules.Error("password","withoutdigits") is not null,"Password requires a digit");
        Check(InputRules.Error("password","12345678") is not null,"Password requires a Latin letter");
        var chart=ReportChart.Build([new Record {Cell1="Посещаемость: 15.10",Values=new(){["report_kind"]="attendance",["total"]="1"}}]).First();
        Check(chart.Points.Single().Display=="1 запись"&&chart.Points.Single().Percent==100,"Graph uses typed values and Russian units");
        Check(!AccessPresentation.Read("Администратор","accounts",["report.view"]),"Hidden section without read permission");
        Check(AccessPresentation.Read("Администратор","reports",["report.view"]),"Available report permission");
        return checks;
    }
}
