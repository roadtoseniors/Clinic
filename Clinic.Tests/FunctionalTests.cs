using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;
using Clinic.API;
using Clinic.Desktop;
using Clinic.Data;
using Clinic.Workspace;
using Microsoft.EntityFrameworkCore;

internal static class FunctionalTests
{
    public static async Task RunAsync()
    {
        var checks=LoginProtectionTests.Run();
        void Check(bool condition,string label){if(!condition)throw new Exception(label);checks++;}
        var root=FindRoot();await using var fixture=await FunctionalDatabase.CreateAsync(root);
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var address=new Uri("http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port);listener.Stop();
        var server=new LocalApiProcess(root,address,fixture.Connection);
        try
        {
            await server.EnsureReadyAsync();
            using var publicApi=new HttpClient{BaseAddress=address};
            async Task<JsonElement> Send(HttpClient client,string method,string path,object? body=null,int status=200)
            {
                using var request=new HttpRequestMessage(new HttpMethod(method),path);
                if(body is not null)request.Content=body is HttpContent content?content:JsonContent.Create(body);
                using var response=await client.SendAsync(request);var text=await response.Content.ReadAsStringAsync();
                if((int)response.StatusCode!=status)throw new Exception(method+" "+path+" expected "+status+", got "+(int)response.StatusCode+": "+text);
                checks++;return string.IsNullOrEmpty(text)?default:JsonDocument.Parse(text).RootElement.Clone();
            }
            async Task<HttpClient> Login(string login,string password="demo12345")
            {
                var auth=await Send(publicApi,"POST","auth/login",new{login,password});
                var client=new HttpClient{BaseAddress=address};client.DefaultRequestHeaders.Authorization=new("Bearer",auth.GetProperty("token").GetString());return client;
            }
            using var admin=await Login("admin");using var registry=await Login("petrova");using var doctor=await Login("sokolov");
            var badLogin="missing_"+Guid.NewGuid().ToString("N")[..10];
            JsonElement captcha=default;
            for(var i=0;i<3;i++)captcha=await Send(publicApi,"POST","auth/login",new{login=badLogin,password="wrong"},401);
            Check(captcha.GetProperty("captchaRequired").GetBoolean(),"HTTP captcha after third failure");
            await Send(publicApi,"POST","auth/login",new{login=badLogin,password="wrong",challengeId="missing",captchaAnswer="AAAAA"},401);
            var refreshed=await Send(publicApi,"POST","auth/captcha",new{login=badLogin});Check(refreshed.GetProperty("imageBase64").GetString()!.Length>100,"HTTP captcha image");
            await using var db=fixture.Context();
            var company=await db.InsuranceCompanies.Select(x=>x.Id).FirstAsync();var district=await db.Districts.Select(x=>x.Id).FirstAsync();
            var doctorId=(await Send(doctor,"GET","api/me")).GetProperty("account").GetProperty("employeeId").GetInt32();
            var patientRole=await db.Roles.Where(r=>r.Code=="patient").Select(r=>r.Id).SingleAsync();
            var name="user_"+Guid.NewGuid().ToString("N")[..10];
            var newUser=await Send(publicApi,"POST","auth/register",new{login=name,password="Clinic1234",lastName="Проверка",firstName="Пациент",birthDate="1990-01-01",gender="М",omsNumber="9000000000000001",omsCompanyId=company});
            var patientId=newUser.GetProperty("userAccount").GetProperty("patientId").GetInt32();
            using var user=await Login(name,"Clinic1234");
            await Send(user,"PUT","api/me/password",new{oldPassword="Clinic1234",newPassword="Clinic5678"},204);
            await Send(user,"GET","api/me",status:401);
            using var changed=await Login(name,"Clinic5678");
            var accountId=newUser.GetProperty("userAccount").GetProperty("id").GetInt32();
            await Send(admin,"PUT","api/accounts/"+accountId,new{roleId=patientRole,isActive=false});
            await Send(changed,"GET","api/me",status:401);
            await Send(admin,"PUT","api/accounts/"+accountId,new{roleId=patientRole,isActive=true});
            using var afterBlock=await Login(name,"Clinic5678");await Send(afterBlock,"POST","api/me/logout",status:204);await Send(afterBlock,"GET","api/me",status:401);
            server.Dispose();server=new LocalApiProcess(root,address,fixture.Connection);await server.EnsureReadyAsync();
            await Send(afterBlock,"GET","api/me",status:401);Check(true,"Logout survives API restart");
            using var current=await Login(name,"Clinic5678");
            var count=await db.Patients.CountAsync();
            object Details(string oms,int? area=null,int? employee=null,string? phone=null)=>new{patient=new{lastName="Новый",firstName="Пациент",birthDate="1991-02-03",gender="Ж",phone},omsNumber=oms,omsCompanyId=company,districtId=area,employeeId=employee};
            await Send(registry,"POST","api/patients/details",Details("abc"),400);Check(await db.Patients.CountAsync()==count,"Invalid OMS does not create a card");
            await Send(registry,"POST","api/patients/details",Details("9000000000000002",phone:"bad"),400);
            await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION fail_test_policy() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF NEW.number='9777000000000001' THEN RAISE EXCEPTION 'functional rollback' USING ERRCODE='23514'; END IF; RETURN NEW; END $$; CREATE TRIGGER fail_test_policy BEFORE INSERT ON patient_policy FOR EACH ROW EXECUTE FUNCTION fail_test_policy();");
            await Send(registry,"POST","api/patients/details",Details("9777000000000001"),400);Check(await db.Patients.CountAsync()==count,"Late policy failure rolls back patient creation");
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_test_policy ON patient_policy; DROP FUNCTION fail_test_policy();");
            var created=await Send(registry,"POST","api/patients/details",Details("9000000000000002"));var otherPatient=created.GetProperty("id").GetInt32();
            await Send(doctor,"GET","api/patients/"+otherPatient,status:403);await Send(doctor,"GET","api/patients/"+otherPatient+"/medcard",status:403);
            await Send(registry,"PUT","api/patients/"+otherPatient+"/details",Details("9000000000000002",district,doctorId));await Send(doctor,"GET","api/patients/"+otherPatient+"/medcard");
            await Send(registry,"PUT","api/patients/"+otherPatient+"/details",Details("9000000000000002"));
            Check(await db.PatientAttachments.AnyAsync(a=>a.PatientId==otherPatient&&a.AttachedTo!=null),"Clearing attachment keeps history");
            await Send(doctor,"GET","api/patients/"+otherPatient+"/medcard",status:403);
            var schedule=await db.Schedules.FirstAsync(s=>s.EmployeeId==doctorId && s.WorkDate<=DateOnly.FromDateTime(DateTime.Today));
            var occupied=await db.Appointments.Where(a=>a.ScheduleId==schedule.Id&&a.Status!="отменена").Select(a=>a.StartTime).ToListAsync();
            var time=SlotRules.All(schedule.StartTime,schedule.EndTime,schedule.SlotMinutes).First(t=>!occupied.Contains(t));
            var appointment=new Clinic.Entities.Appointment{ScheduleId=schedule.Id,PatientId=patientId,StartTime=time,Status="запланирована",Source="регистратура",CreatedAt=DateTime.Now};db.Appointments.Add(appointment);await db.SaveChangesAsync();
            var visit=await Send(doctor,"POST","api/visits",new{appointmentId=appointment.Id},201);var visitId=visit.GetProperty("id").GetInt32();
            await Send(doctor,"PUT","api/visits/"+visitId,new{complaints="Проверка",diagnosisCode="J06.9"});
            var drugId=await db.Drugs.Select(d=>d.Id).FirstAsync();var serviceId=await db.Services.Where(s=>s.IsActive).Select(s=>s.Id).FirstAsync();
            var rx=await Send(doctor,"POST",$"api/visits/{visitId}/prescriptions",new{drugId,dosage="1",instruction="Инструкция",isRecipe=false},201);
            await Send(doctor,"PUT","api/prescriptions/"+rx.GetProperty("id").GetInt32(),new{drugId,dosage="2",instruction="Изменено",isRecipe=true});
            var referral=await Send(doctor,"POST",$"api/visits/{visitId}/referrals",new{serviceId},201);var referralId=referral.GetProperty("id").GetInt32();
            MultipartFormDataContent Files(bool bad=false)
            {
                var form=new MultipartFormDataContent();form.Add(new StringContent("Готово"),"result");
                foreach(var file in bad?new[]{("first.pdf","application/pdf"),("second.exe","application/octet-stream")}:new[]{("first.pdf","application/pdf"),("second.pdf","application/pdf")})
                {var content=new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF-1.4 fixture"));content.Headers.ContentType=new(file.Item2);form.Add(content,"files",file.Item1);}return form;
            }
            await Send(doctor,"POST",$"api/referrals/{referralId}/complete",Files(true),400);Check(await db.ReferralFiles.CountAsync(f=>f.ReferralId==referralId)==0,"Invalid second file rolls back first file");
            await Send(doctor,"POST",$"api/referrals/{referralId}/complete",Files());Check(await db.ReferralFiles.CountAsync(f=>f.ReferralId==referralId)==2,"Result saves both files");
            Check(await db.Notifications.AnyAsync(n=>n.PatientId==patientId&&n.Message.Contains("Готов результат")),"Result notification saved");
            var draft=await Send(doctor,"POST",$"api/visits/{visitId}/referrals",new{serviceId},201);var draftId=draft.GetProperty("id").GetInt32();
            using(var form=new MultipartFormDataContent())
            {
                var file=new ByteArrayContent(Encoding.UTF8.GetBytes("%PDF fixture"));file.Headers.ContentType=new("application/pdf");form.Add(file,"file","draft.pdf");
                await Send(doctor,"POST",$"api/referrals/{draftId}/files",form,201);
            }
            await Send(doctor,"DELETE","api/referrals/"+draftId,status:204);
            Check(!await db.ReferralFiles.AnyAsync(f=>f.ReferralId==draftId),"Deleting uncompleted referral cascades to its files");
            await Send(doctor,"POST",$"api/visits/{visitId}/close");await Send(doctor,"PUT","api/prescriptions/"+rx.GetProperty("id").GetInt32(),new{drugId,dosage="3",instruction="Поздно",isRecipe=false},409);
            var first=await Send(registry,"POST",$"api/visits/{visitId}/services",new{serviceId,quantity=1},201);var firstId=first.GetProperty("id").GetInt32();
            var second=await Send(registry,"POST",$"api/visits/{visitId}/services",new{serviceId,quantity=2},201);var secondId=second.GetProperty("id").GetInt32();
            await Send(registry,"PUT","api/visit-services/"+secondId,new{serviceId,quantity=3});
            using var gateway=new HttpClinicGateway(address.ToString());var signed=await gateway.SignInAsync("petrova","demo12345");await gateway.ActAsync(signed!.Value.Session,"create_invoice",firstId);
            var invoiceId=await db.VisitServices.Where(v=>v.Id==firstId).Select(v=>v.InvoiceId).SingleAsync();
            Check(invoiceId.HasValue&&await db.VisitServices.CountAsync(v=>v.InvoiceId==invoiceId)==1&&await db.VisitServices.AnyAsync(v=>v.Id==secondId&&v.InvoiceId==null),"UI action invoices selected service only");
            await Send(registry,"POST",$"api/invoices/{invoiceId}/cancel",status:204);Check(await db.VisitServices.AnyAsync(v=>v.Id==firstId&&v.InvoiceId==null),"Cancel invoice releases services");
            await Send(registry,"DELETE","api/visit-services/"+secondId,status:204);
            foreach(var login in new[]{"kirillov","petrova","sokolov","admin"})
            {
                using var g=new HttpClinicGateway(address.ToString());var session=(await g.SignInAsync(login,"demo12345"))!.Value.Session;
                foreach(var section in Catalog.Sections(session.Role).Where(s=>s.Key is not ("home" or "backup"))){await g.ListAsync(session,section.Key);checks++;}
            }
            Check(await db.Audits.AnyAsync(a=>a.TableName=="invoice"),"Invoices are audited");
            var roles=await Send(admin,"GET","api/roles");
            var doctorRole=roles.EnumerateArray().First(r=>r.GetProperty("code").GetString()=="doctor");
            var doctorRoleId=doctorRole.GetProperty("id").GetInt32();var originalPermissions=doctorRole.GetProperty("permissions").EnumerateArray().Select(p=>p.GetString()!).ToArray();
            await Send(admin,"PUT",$"api/roles/{doctorRoleId}/permissions",new{codes=originalPermissions.Append("report.view").Distinct().ToArray()});
            await Send(doctor,"GET","api/me",status:401);
            using var newDoctor=await Login("sokolov");await Send(newDoctor,"GET","api/reports/attendance");
            await Send(admin,"PUT",$"api/roles/{doctorRoleId}/permissions",new{codes=originalPermissions});
            var previous=Environment.GetEnvironmentVariable("CLINIC_API_URL");Environment.SetEnvironmentVariable("CLINIC_API_URL",address.ToString());
            try {await ApiIntegration.RunAsync(new Npgsql.NpgsqlConnectionStringBuilder(fixture.Connection).Database,fixture.Connection);}
            finally {Environment.SetEnvironmentVariable("CLINIC_API_URL",previous);}
            using var restoreGateway=new HttpClinicGateway(address.ToString());await restoreGateway.SignInAsync("admin","demo12345");
            var marker=await restoreGateway.BackupAsync((await restoreGateway.SignInAsync("admin","demo12345"))!.Value.Session);
            var epoch=await RuntimeSchema.EpochAsync(db,default);var probe="restore_probe_"+Guid.NewGuid().ToString("N");
            db.Notifications.Add(new Clinic.Entities.Notification{PatientId=patientId,CreatedAt=DateTime.Now,Message=probe,IsRead=false});await db.SaveChangesAsync();
            using var oldSession=await Login("admin");
            await restoreGateway.RestoreAsync((await restoreGateway.SignInAsync("admin","demo12345"))!.Value.Session,marker);
            Check(!await db.Notifications.AnyAsync(n=>n.Message==probe),"Restore replaces data on temporary database");
            Check(await RuntimeSchema.EpochAsync(db,default)!=epoch,"Restore resets token epoch");
            await Send(oldSession,"GET","api/me",status:401);using var afterRestore=await Login("admin");await Send(afterRestore,"GET","api/me");
            Console.WriteLine($"PASS: {checks} checks (isolated HTTP + PostgreSQL functional regression)");
        }
        finally {server.Dispose();}
    }
    private static string FindRoot(){for(var d=new DirectoryInfo(AppContext.BaseDirectory);d is not null;d=d.Parent)if(File.Exists(Path.Combine(d.FullName,"Clinic.slnx")))return d.FullName;throw new Exception("Source root missing");}
}
