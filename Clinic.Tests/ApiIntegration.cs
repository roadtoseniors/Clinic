using Clinic.Data;
using Clinic.ViewModels;
using Clinic.Workspace;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;

public static class ApiIntegration
{
    public static async Task RunAsync()
    {
        var url=Environment.GetEnvironmentVariable("CLINIC_API_URL")??"http://127.0.0.1:5278";
        if(!Uri.TryCreate(url,UriKind.Absolute,out var uri)||!uri.IsLoopback)throw new Exception("API checks run only against a loopback test server.");
        using var ping=new HttpClient();var environment=await ping.GetFromJsonAsync<Dictionary<string,string>>(url.TrimEnd('/')+"/health");
        if(environment?.GetValueOrDefault("database")!="clinic_api_integration_test")throw new Exception("API checks require clinic_api_integration_test.");
        await ApiFixture.PrepareTodayAsync();
        var checks=0;void Assert(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
        async Task Denied(Func<Task> work,string label){try{await work();}catch(InvalidOperationException){checks++;return;}throw new Exception("Expected rejection: "+label);}
        async Task<(HttpClinicGateway Gateway,ClinicSession Session)> Login(string login)
        {
            var g=new HttpClinicGateway(url);var result=await g.SignInAsync(login,"demo12345");Assert(result is not null,"Login "+login);return (g,result!.Value.Session);
        }
        var (p,patient)=await Login("kirillov");var (r,registrar)=await Login("petrova");var (d,doctor)=await Login("sokolov");var (a,admin)=await Login("admin");
        using(p)using(r)using(d)using(a)
        {
            foreach(var (g,s) in new[]{(p,patient),(r,registrar),(d,doctor),(a,admin)})foreach(var section in Catalog.Sections(s.Role).Where(s=>s.Key is not ("home" or "backup"))){await g.ListAsync(s,section.Key);checks++;}
            var unique=Guid.NewGuid().ToString("N")[..10];using var publicApi=new HttpClinicGateway(url);var publicChoices=await publicApi.RegistrationChoicesAsync();
            using(var registration=new HttpClinicGateway(url))
            {
                var values=new Dictionary<string,string>{["last_name"]="Демонстрационный",["first_name"]="Пациент",["birth_date"]="1990-01-01",["gender"]="М",["oms_policy"]="TEST-"+unique,["oms_company_id"]=publicChoices["companies"][0].Id,["login"]="test_"+unique,["password"]="demo12345",["password_confirmation"]="demo12345"};
                await registration.RegisterAsync(values);var registered=await registration.SignInAsync(values["login"],values["password"]);Assert(registered?.Session.Role=="Пациент","Registration creates a real patient account");
                await registration.SaveAsync(registered!.Value.Session,new("password",0,new(){["old_password"]="demo12345",["new_password"]="demo123456",["confirmation"]="demo123456"}));
                using var relogin=new HttpClinicGateway(url);Assert(await relogin.SignInAsync(values["login"],"demo123456") is not null,"Changed password persists in PostgreSQL");
            }
            var ac=await a.ChoicesAsync(admin);
            var serviceId=await a.SaveAsync(admin,new("service",0,new(){["name"]="Демо услуга "+unique,["group_id"]=ac["groups"][0].Id,["price"]="123.45"}));
            await a.SaveAsync(admin,new("service",serviceId,new(){["name"]="Демо услуга "+unique,["group_id"]=ac["groups"][0].Id,["price"]="234.56"}));
            Assert((await a.ListAsync(admin,"services")).First(v=>v.Id==serviceId).Cell3=="234.56 ₽","Service edit uses API list and update routes");
            await a.ActAsync(admin,"toggle_service",serviceId);Assert((await a.ListAsync(admin,"services")).First(v=>v.Id==serviceId).Status=="архив","Service archive persisted");
            await a.ActAsync(admin,"toggle_service",serviceId);Assert((await a.ListAsync(admin,"services")).First(v=>v.Id==serviceId).Status=="активна","Service restore persisted");
            var createdPatient=await r.SaveAsync(registrar,new("patient",0,new(){["last_name"]="Демо",["first_name"]="Новый",["birth_date"]="1990-01-01",["gender"]="Ж",["oms_policy"]="PAT-"+unique,["oms_company_id"]=publicChoices["companies"][0].Id,["district_id"]=ac["districts"][0].Id,["primary_doctor_id"]=doctor.EmployeeId.ToString()!}));
            var newCard=(await r.ListAsync(registrar,"patients")).First(v=>v.Id==createdPatient);Assert(newCard.Values["oms_policy"]=="PAT-"+unique&&newCard.Values["district_id"]==ac["districts"][0].Id,"Patient, policy and attachment saved through API");
            await r.ActAsync(registrar,"toggle_patient",createdPatient);Assert((await r.ListAsync(registrar,"patients")).First(v=>v.Id==createdPatient).Status=="архив","Patient archive persisted");
            await r.ActAsync(registrar,"toggle_patient",createdPatient);
            Assert((await p.ListAsync(patient,"patients")).Single().Id==patient.PatientId,"Own card only");
            await Denied(()=>p.ListAsync(patient,"accounts"),"Patient accounts denied");
            await Denied(()=>p.ListAsync(patient with{PatientId=patient.PatientId+1},"patients"),"Forged session denied");
            await p.SaveUserThemeAsync(patient.AccountId,true);using(var again=new HttpClinicGateway(url)){var logged=await again.SignInAsync("kirillov","demo12345");Assert(await again.GetUserThemeAsync(logged!.Value.Session.AccountId)==true,"Theme persisted through a new client session");}
            Assert(await d.GetUserThemeAsync(doctor.AccountId)==false,"Theme isolated from doctor");await p.SaveUserThemeAsync(patient.AccountId,false);
            var slot=(await p.ListAsync(patient,"slots")).First(s=>DateTime.Parse(s.Values["work_date"])>=DateTime.Today.AddDays(1));
            var booking=await p.SaveAsync(patient,new("booking",0,new(){["patient_id"]=patient.PatientId.ToString()!,["slot"]=slot.Values["slot"]}));
            await Denied(()=>r.SaveAsync(registrar,new("booking",0,new(){["patient_id"]="2",["slot"]=slot.Values["slot"]})),"Occupied slot denied by server");
            var next=(await p.ListAsync(patient,"slots")).First(s=>s.Values["work_date"]==slot.Values["work_date"]);
            await p.SaveAsync(patient,new("booking",booking,new(){["patient_id"]=patient.PatientId.ToString()!,["slot"]=next.Values["slot"]}));
            Assert((await p.ListAsync(patient,"slots")).Any(s=>s.Values["slot"]==slot.Values["slot"]),"Reschedule frees old slot");
            await p.ActAsync(patient,"cancel",booking);Assert((await p.ListAsync(patient,"slots")).Any(s=>s.Values["slot"]==next.Values["slot"]),"Cancellation frees slot");
            var ap=(await d.ListAsync(doctor,"appointments")).First(x=>x.Status=="запланирована"&&x.Values["work_date"]==DateTime.Today.ToString("yyyy-MM-dd"));
            await d.ActAsync(doctor,"start_visit",ap.Id);var visit=(await d.ListAsync(doctor,"visits")).First(v=>v.Values["appointment_id"]==ap.Id.ToString());
            await d.SaveAsync(doctor,new("visit",visit.Id,new(){["complaints"]="Демо проверка HTTP",["anamnesis"]="Демонстрационные данные",["objective_status"]="Тест",["diagnosis_code"]="J06.9",["recommendations"]="Тест"}));
            var choices=await d.ChoicesAsync(doctor);
            var prescription=await d.SaveAsync(doctor,new("prescription",0,new(){["visit_id"]=visit.Id.ToString(),["drug_id"]=choices["drugs"][0].Id,["dosage"]="Демо",["instruction"]="Демо",["is_recipe"]="false"}));
            Assert((await p.ListAsync(patient,"prescriptions")).Any(v=>v.Id==prescription),"Patient reads saved prescription");
            var referral=await d.SaveAsync(doctor,new("referral",0,new(){["visit_id"]=visit.Id.ToString(),["service_id"]=choices["services"][0].Id}));
            var pdf=Encoding.UTF8.GetBytes("%PDF-1.4\n%fictional integration fixture\n%%EOF");
            await d.SaveAsync(doctor,new("result",referral,new(){["result"]="Тестовый результат API",["file_name"]="demo.pdf",["file_data"]=Convert.ToBase64String(pdf)}));
            Assert((await p.AttachmentAsync(patient,referral))!.SequenceEqual(pdf),"File saved in PostgreSQL and downloaded by patient");
            await d.ActAsync(doctor,"close_visit",visit.Id);await Denied(()=>d.SaveAsync(doctor,new("visit",visit.Id,new(){["complaints"]="late",["diagnosis_code"]="J06.9"})),"Closed protocol protected by API");
            var rc=await r.ChoicesAsync(registrar);var rendered=await r.SaveAsync(registrar,new("rendered",0,new(){["visit_id"]=visit.Id.ToString(),["service_id"]=rc["services"][0].Id,["quantity"]="1"}));
            await r.ActAsync(registrar,"create_invoice",rendered);var service=(await r.ListAsync(registrar,"rendered")).First(x=>x.Id==rendered);var invoice=int.Parse(service.Values["invoice_id"]);
            await r.SaveAsync(registrar,new("payment",invoice,new(){["method_id"]=rc["methods"].First(c=>c.Name=="Наличные").Id,["policy_id"]=""}));
            Assert((await p.ListAsync(patient,"invoices")).First(x=>x.Id==invoice).Status=="оплачен","Invoice saved and paid through API");
            var document=await r.SaveAsync(registrar,new("document",0,new(){["patient_id"]=patient.PatientId.ToString()!,["type_id"]=rc["document_types"][0].Id,["visit_id"]="",["content"]="Фиктивный документ HTTP проверки"}));
            Assert((await p.ListAsync(patient,"documents")).Any(x=>x.Id==document),"Document saved and visible to patient");
            Assert((await a.ListAsync(admin,"reports",DateTime.Today,DateTime.Today)).Count>0,"API reports");
            Assert((await a.ListAsync(admin,"audit")).Any(x=>x.Cell3=="UPDATE"),"Server audit records actual changes");
            using var backup=System.Text.Json.JsonDocument.Parse(await a.BackupAsync(admin));
            Assert(backup.RootElement.GetProperty("kind").GetString()=="clinic-server-backup"&&backup.RootElement.GetProperty("fileName").GetString()!.EndsWith(".backup"),"Server pg_dump backup created");
            var note=(await p.ListAsync(patient,"notifications")).First(x=>x.Status=="новое");await p.ActAsync(patient,"mark_read",note.Id);Assert((await p.ListAsync(patient,"notifications")).First(x=>x.Id==note.Id).Status=="прочитано","Notification read persisted");
            var vm=new MainViewModel{ApiAddress=url,Login="sokolov",Password="wrong"};await vm.SignInCommand.ExecuteAsync(null);Assert(vm.HasStatusMessage&&!vm.IsSignedIn,"Login error remains visible");
            vm.Password="demo12345";await vm.SignInCommand.ExecuteAsync(null);Assert(vm.IsSignedIn&&vm.Workspace?.Gateway is HttpClinicGateway,"ViewModel uses live API");await vm.SignOutCommand.ExecuteAsync(null);
        }
        Console.WriteLine($"PASS: {checks} checks (HTTP API + PostgreSQL)");
    }
}
