using Clinic.Data;
using Clinic.Demo;
using Clinic.ViewModels;
using Clinic.Workspace;

if(args.Contains("--seed-api")){await ApiFixture.SeedAsync();return;}
if(args.Contains("--api")){await ApiIntegration.RunAsync();return;}
if(args.Contains("--functional")){await FunctionalTests.RunAsync();return;}
if(args.Contains("--startup")){await LocalApiStartup.RunAsync();return;}
if(args.Contains("--startup-child")){await LocalApiStartup.RunChildAsync(args);return;}
if(args.Contains("--export-samples")){Console.WriteLine($"PASS: {await ExportTests.RunAsync(Path.GetFullPath("tmp/checklist-export-2026-10-20"))} checks (Word and ClosedXML exports)");return;}
int assertions=LoginProtectionTests.Run()+await UiFlowTests.RunAsync()+await PresentationTests.RunAsync()+await ExportTests.RunAsync();
void Assert(bool condition,string message){if(!condition)throw new Exception(message);assertions++;}
async Task Denied(Func<Task> work,string label){try{await work();}catch(InvalidOperationException){assertions++;return;}catch(Npgsql.PostgresException ex) when(ex.SqlState=="23505"){assertions++;return;}throw new Exception("Expected rejection: "+label);}
IClinicGateway gateway=new DemoGateway();
if(args.Contains("--database"))throw new InvalidOperationException("Используйте --functional: проверки БД выполняются в отдельной временной базе через API.");
async Task<ClinicSession> Login(string login)
{
    var result=await gateway.SignInAsync(login,"demo123");Assert(result is not null,"Login: "+login);return result!.Value.Session;
}
var patient=await Login("kirillov");var registrar=await Login("petrova");var doctor=await Login("sokolov");var admin=await Login("admin");
Assert(await gateway.SignInAsync("kirillov","wrong") is null,"Invalid password");
foreach(var session in new[]{patient,registrar,doctor,admin})
{
    foreach(var page in Catalog.Sections(session.Role).Where(p=>p.Key is not ("home" or "backup")))
    {
        await gateway.ListAsync(session,page.Key);assertions++;
    }
}
var ownPatients=await gateway.ListAsync(patient,"patients");Assert(ownPatients.Count==1&&ownPatients[0].Id==patient.PatientId,"Patient isolation");
await Denied(()=>gateway.ListAsync(patient,"accounts"),"Patient cannot read accounts");
await Denied(()=>gateway.ListAsync(patient with {PatientId=2},"patients"),"Session owner cannot be forged");
await Denied(()=>gateway.SaveAsync(patient,new("service",0,new(){["name"]="Bad",["group_id"]="1",["price"]="10"})),"Patient cannot edit price");
await gateway.SaveUserThemeAsync(patient.AccountId,true);Assert(await gateway.GetUserThemeAsync(patient.AccountId)==true,"User theme");
var originalTheme=await gateway.GetUserThemeAsync(doctor.AccountId);await gateway.SaveUserThemeAsync(patient.AccountId,false);Assert(await gateway.GetUserThemeAsync(doctor.AccountId)==originalTheme,"Other user's theme unchanged");
var slots=await gateway.ListAsync(patient,"slots");Assert(slots.Count>2,"Future slots");
// Choose a tomorrow slot so a fixture's existing patient bookings don't conflict.
var slot=slots.First(s=>DateTime.Parse(s.Values["work_date"])>=DateTime.Today.AddDays(1));
var booking=new Dictionary<string,string>{["patient_id"]=patient.PatientId!.Value.ToString(),["slot"]=slot.Values["slot"]};
var bookingId=await gateway.SaveAsync(patient,new("booking",0,booking));
Assert((await gateway.ListAsync(patient,"appointments")).Any(a=>a.Id==bookingId),"New booking visible");
await Denied(()=>gateway.SaveAsync(registrar,new("booking",0,new(){["patient_id"]="2",["slot"]=slot.Values["slot"]})),"Double booking blocked");
var next=(await gateway.ListAsync(patient,"slots")).First(r=>r.Values["work_date"]==slot.Values["work_date"]&&r.Values["slot"]!=slot.Values["slot"]);
await gateway.SaveAsync(patient,new("booking",bookingId,new(){["patient_id"]=patient.PatientId.Value.ToString(),["slot"]=next.Values["slot"]}));
Assert((await gateway.ListAsync(patient,"slots")).Any(r=>r.Values["slot"]==slot.Values["slot"]),"Reschedule releases old slot");
await gateway.ActAsync(patient,"cancel",bookingId);
Assert((await gateway.ListAsync(patient,"slots")).Any(r=>r.Values["slot"]==next.Values["slot"]),"Cancel releases slot");
var appointments=await gateway.ListAsync(doctor,"appointments");var today=appointments.First(a=>a.Status=="запланирована"&&DateTime.Parse(a.Values["work_date"])<=DateTime.Today);
await gateway.ActAsync(doctor,"start_visit",today.Id);
var visit=(await gateway.ListAsync(doctor,"visits")).First(v=>v.Values["appointment_id"]==today.Id.ToString());
await gateway.ActAsync(doctor,"start_visit",today.Id);
Assert((await gateway.ListAsync(doctor,"visits")).Count(v=>v.Values["appointment_id"]==today.Id.ToString())==1,"Opening visit is idempotent");
await Denied(()=>gateway.ActAsync(registrar,"cancel",today.Id),"Started appointment cannot be cancelled");
await gateway.SaveAsync(doctor,new("visit",visit.Id,new(){["complaints"]="Проверка сценария",["anamnesis"]="Тест",["objective_status"]="Тест",["diagnosis_code"]="J06.9",["recommendations"]="Тестовый протокол"}));
var choices=await gateway.ChoicesAsync(doctor);
var prescription=await gateway.SaveAsync(doctor,new("prescription",0,new(){["visit_id"]=visit.Id.ToString(),["drug_id"]=choices["drugs"][0].Id,["dosage"]="Тест",["instruction"]="Тест",["is_recipe"]="false",["valid_until"]=DateTime.Today.AddDays(10).ToString("yyyy-MM-dd")}));
var referral=await gateway.SaveAsync(doctor,new("referral",0,new(){["visit_id"]=visit.Id.ToString(),["service_id"]=choices["services"][0].Id,["consultation_doctor_id"]=""}));
await gateway.SaveAsync(doctor,new("result",referral,new(){["result"]="Демонстрационный результат проверки",["file_name"]="result.txt",["file_data"]=Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("fixture"))}));
Assert(System.Text.Encoding.UTF8.GetString((await gateway.AttachmentAsync(doctor,referral))!)=="fixture","Result file roundtrip");
await gateway.ActAsync(doctor,"close_visit",visit.Id);
await Denied(()=>gateway.SaveAsync(doctor,new("visit",visit.Id,new(){["complaints"]="Late edit",["diagnosis_code"]="J06.9"})),"Closed visit immutable");
Assert((await gateway.ListAsync(doctor,"appointments")).First(a=>a.Id==today.Id).Status=="завершена","Visit closes appointment");
var registrarChoices=await gateway.ChoicesAsync(registrar);
var rendered=await gateway.SaveAsync(registrar,new("rendered",0,new(){["visit_id"]=visit.Id.ToString(),["service_id"]=registrarChoices["services"][0].Id,["quantity"]="1"}));
await gateway.ActAsync(registrar,"create_invoice",rendered);
var service=(await gateway.ListAsync(registrar,"rendered")).First(s=>s.Id==rendered);var invoiceId=int.Parse(service.Values["invoice_id"]);
var bill=(await gateway.ListAsync(registrar,"invoices")).First(b=>b.Id==invoiceId);var amount=decimal.Parse(bill.Values["total_text"],System.Globalization.CultureInfo.InvariantCulture);
var method=registrarChoices["methods"].First(m=>m.Name=="Наличные").Id;
var half=(amount/2).ToString(System.Globalization.CultureInfo.InvariantCulture);
await gateway.SaveAsync(registrar,new("payment",invoiceId,new(){["amount"]=half,["method_id"]=method}));
Assert((await gateway.ListAsync(registrar,"invoices")).First(b=>b.Id==invoiceId).Status=="не оплачен","Partial payment keeps balance");
await gateway.SaveAsync(registrar,new("payment",invoiceId,new(){["amount"]=half,["method_id"]=method}));
Assert((await gateway.ListAsync(registrar,"invoices")).First(b=>b.Id==invoiceId).Status=="оплачен","Full payment settles invoice");
await Denied(()=>gateway.SaveAsync(registrar,new("payment",invoiceId,new(){["amount"]="1",["method_id"]=method})),"Overpayment denied");
var document=await gateway.SaveAsync(registrar,new("document",0,new(){["patient_id"]=today.Values["patient_id"],["kind"]="Согласие на обработку данных",["content"]="Фиктивный документ для проверки"}));
Assert((await gateway.ListAsync(registrar,"documents")).Any(d=>d.Id==document),"Document saved");
await Denied(()=>gateway.SaveAsync(registrar,new("document",document,new(){["patient_id"]=today.Values["patient_id"],["kind"]="Согласие на обработку данных",["content"]="Overwrite"})),"Issued document immutable");
await Denied(()=>gateway.ActAsync(admin,"toggle_account",admin.AccountId),"Admin cannot block own account");
var backup=await gateway.BackupAsync(admin);Assert(backup.Length>1000,"Backup contains data");await gateway.RestoreAsync(admin,backup);
Assert((await gateway.ListAsync(doctor,"visits")).Any(v=>v.Id==visit.Id&&v.Status=="закрыт"),"Backup roundtrip");
{
    var independent=new DemoGateway();
    var patientDashboard=(await independent.SignInAsync("kirillov","demo123"))!.Value.Dashboard;
    Assert(patientDashboard.Metrics.Any(m=>m.Label=="Посещения"&&m.Value=="1"),"Patient dashboard shows own visits");
    var independentAdmin=(await independent.SignInAsync("admin","demo123"))!.Value.Session;
    var historicalReports=await independent.ListAsync(independentAdmin,"reports",DateTime.Today.AddDays(-100),DateTime.Today.AddDays(-50));
    Assert(historicalReports.First(r=>r.Id==1).Cell2=="0","Report respects date range");
    await Denied(()=>independent.ListAsync(independentAdmin,"reports",DateTime.Today,DateTime.Today.AddDays(-1)),"Invalid report range rejected");
    await independent.RegisterAsync(new(){["oms_policy"]="0000000000000004",["birth_date"]="1995-09-17",["login"]="new_patient",["password"]="demo-pass-4"});
    var registered=await independent.SignInAsync("new_patient","demo-pass-4");
    Assert(registered?.Session.PatientId==4,"Registration links existing patient card");
    await Denied(()=>independent.RegisterAsync(new(){["oms_policy"]="0000000000000002",["birth_date"]="1992-07-21",["login"]="NEW_PATIENT",["password"]="demo-pass-2"}),"Case insensitive login uniqueness");
    var vm=new MainViewModel(() => new DemoGateway()){Login="sokolov",Password="demo123"};await vm.SignInCommand.ExecuteAsync(null);
    Assert(vm.IsSignedIn&&vm.Workspace is not null,"View model login and workspace");
    vm.Workspace!.SelectedSection=vm.Workspace.Sections.First(s=>s.Key=="appointments");await vm.Workspace.RefreshCommand.ExecuteAsync(null);
    vm.Workspace.SelectedRow=vm.Workspace.Rows.First(r=>r.Status=="запланирована"&&DateTime.Parse(r.Values["work_date"])<=DateTime.Today);
    var startedAppointmentId = vm.Workspace.SelectedRow.Id;
    await vm.Workspace.RowActionCommand.ExecuteAsync("start_visit");Assert(vm.Workspace.IsEditorOpen&&vm.Workspace.Fields.Count>0,"Doctor opens editor through UI commands");
    vm.Workspace.CloseEditorCommand.Execute(null);
    var workspace = vm.Workspace;
    await workspace.ChangePasswordCommand.ExecuteAsync(null);
    workspace.Fields.First(f=>f.Spec.Key=="new_password").Text="unsaved-password";
    var closeEditor=workspace.CloseEditorCommand.ExecuteAsync(null);
    Assert(workspace.IsActionConfirmOpen&&!closeEditor.IsCompleted,"Dirty form requires discard confirmation");
    workspace.CancelActionCommand.Execute(null);await closeEditor;
    Assert(workspace.IsEditorOpen&&workspace.HasUnsavedChanges,"Cancelling discard preserves changes");
    var signout=vm.SignOutCommand.ExecuteAsync(null);
    workspace.CancelActionCommand.Execute(null);await signout;
    Assert(vm.IsSignedIn&&workspace.HasUnsavedChanges,"Cancelling signout preserves session and editor");
    closeEditor=workspace.CloseEditorCommand.ExecuteAsync(null);workspace.ConfirmActionCommand.Execute(null);await closeEditor;
    Assert(!workspace.IsEditorOpen,"Confirmed discard closes form");
    workspace.SelectedSection=workspace.Sections.First(s=>s.Key=="appointments");await workspace.RefreshCommand.ExecuteAsync(null);
    await workspace.SetDateRangeCommand.ExecuteAsync("all");
    workspace.SelectedRow=workspace.Rows.First(r=>r.Status=="запланирована" && r.Id != startedAppointmentId);
    var appointmentId=workspace.SelectedRow.Id;
    var noShow=workspace.RowActionCommand.ExecuteAsync("no_show");
    Assert(workspace.IsActionConfirmOpen&&!noShow.IsCompleted,"Attendance changes require confirmation");
    workspace.CancelActionCommand.Execute(null);await noShow;
    Assert((await workspace.Gateway.ListAsync(workspace.Session,"appointments")).First(r=>r.Id==appointmentId).Status=="запланирована","Cancelling confirmation leaves appointment unchanged");
    noShow=workspace.RowActionCommand.ExecuteAsync("no_show");workspace.ConfirmActionCommand.Execute(null);await noShow;
    Assert(workspace.Rows.First(r=>r.Id==appointmentId).Status=="неявка","Confirmed attendance change is saved");
    workspace.SortOrder="Сначала новые";
    Assert(workspace.Rows.Select(r=>r.Values.GetValueOrDefault("work_date","")).SequenceEqual(workspace.Rows.Select(r=>r.Values.GetValueOrDefault("work_date","")).OrderDescending()),"Date sorting is chronological");
    workspace.Search="No matching patient";Assert(workspace.IsEmpty&&workspace.EmptyTitle=="Ничего не найдено","Filtered empty state is distinct");
    workspace.ResetFiltersCommand.Execute(null);Assert(workspace.HasRows,"Reset restores rows");
    var invalidFields=new[]{new EditorField(new("birth_date","Дата рождения",true),DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"))};
    await Denied(()=>{FormValidation.Validate(invalidFields);return Task.CompletedTask;},"Future birth date rejected before request");
    var passwords=new[]{new EditorField(new("new_password","Пароль",true,Secret:true),"password-one"),new EditorField(new("confirmation","Повтор",true,Secret:true),"password-two")};
    await Denied(()=>{FormValidation.Validate(passwords);return Task.CompletedTask;},"Password confirmation checked before request");
    var confirmation=vm.Workspace.ConfirmRestoreAsync();vm.Workspace.ConfirmRestoreCommand.Execute(null);
    Assert(!confirmation.IsCompleted,"Restore requires typed confirmation");
    vm.Workspace.RestoreConfirmText="ВОССТАНОВИТЬ";vm.Workspace.ConfirmRestoreCommand.Execute(null);
    Assert(await confirmation&&!vm.Workspace.IsRestoreConfirmOpen,"Confirmed restore closes overlay");
    var cancelled=vm.Workspace.ConfirmRestoreAsync();vm.Workspace.CancelRestoreCommand.Execute(null);Assert(!await cancelled,"Restore can be cancelled");
    await TestUi.SignOutAsync(vm);Assert(vm.IsSignedOut&&vm.Workspace is null,"Sign out clears workspace");
    await vm.OpenRegistrationCommand.ExecuteAsync(null);
    vm.RegistrationFields.First(f=>f.Spec.Key=="login").Text="unsaved_registration";
    var registrationClose=vm.CloseRegistrationCommand.ExecuteAsync(null);
    Assert(vm.IsRegistrationDiscardOpen&&!registrationClose.IsCompleted,"Registration requires discard confirmation");
    vm.CancelRegistrationDiscardCommand.Execute(null);await registrationClose;
    Assert(vm.IsRegistrationOpen&&vm.HasRegistrationChanges,"Registration values survive cancelled discard");
    registrationClose=vm.CloseRegistrationCommand.ExecuteAsync(null);vm.ConfirmRegistrationDiscardCommand.Execute(null);await registrationClose;
    Assert(!vm.IsRegistrationOpen&&vm.RegistrationFields.Count==0,"Confirmed registration discard clears fields");

}
Console.WriteLine($"PASS: {assertions} checks ({(args.Contains("--database")?"PostgreSQL":"independent demo")})");
