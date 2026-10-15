using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Clinic.Demo;
using Clinic.Workspace;

namespace Clinic.Data;

public sealed partial class HttpClinicGateway : IClinicGateway, IDisposable
{
    private readonly HttpClient _http;
    private ClinicSession? _session;
    public IReadOnlyList<string> Permissions { get; private set; }=[];
    public HttpClinicGateway(string address, HttpClient? client=null)
    {
        if(!Uri.TryCreate(address.TrimEnd('/')+"/",UriKind.Absolute,out var uri)||uri.Scheme is not ("http" or "https")||uri.UserInfo.Length>0)
            throw new InvalidOperationException("Некорректные настройки подключения. Обратитесь к администратору.");
        _http=client??new HttpClient();_http.BaseAddress=uri;_http.Timeout=TimeSpan.FromSeconds(30);
    }
    public void Dispose()=>_http.Dispose();
    private static JsonElement P(JsonElement value,string key)=>value.ValueKind==JsonValueKind.Object&&value.TryGetProperty(key,out var result)?result:default;
    private static string S(JsonElement value,string key)=>P(value,key).ValueKind is JsonValueKind.Undefined or JsonValueKind.Null?"":P(value,key).ToString();
    private static int I(JsonElement value,string key)=>int.TryParse(S(value,key),out var id)?id:0;
    private static bool B(JsonElement value,string key)=>S(value,key)=="True"||S(value,key)=="true";
    private static List<JsonElement> A(JsonElement value)=>value.ValueKind==JsonValueKind.Array?value.EnumerateArray().Select(x=>x.Clone()).ToList():[];
    private static string FullName(JsonElement v)=>string.Join(' ',new[]{S(v,"lastName"),S(v,"firstName"),S(v,"middleName")}.Where(x=>x.Length>0));
    private static string DisplayDate(string value)=>DateTime.TryParse(value,CultureInfo.InvariantCulture,out var d)?d.ToString("dd.MM.yyyy"):value;
    private static string Time(JsonElement value,string key)=>S(value,key).Length>=5?S(value,key)[..5]:S(value,key);
    private static string Money(JsonElement v,string key)=>S(v,key)+" ₽";
    private static string Snake(string key)=>string.Concat(key.Select((c,i)=>char.IsUpper(c)&&i>0?"_"+char.ToLowerInvariant(c):char.ToLowerInvariant(c).ToString()));
    private static Record Row(JsonElement json,string c1,string c2,string c3,string c4,string status,string details="",Dictionary<string,string>? extra=null)
    {
        var values=json.ValueKind==JsonValueKind.Object?json.EnumerateObject().Where(p=>p.Value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object)).ToDictionary(p=>Snake(p.Name),p=>p.Value.ValueKind==JsonValueKind.Null?"":p.Value.ToString()):new Dictionary<string,string>();
        foreach(var key in values.Keys.ToArray())if(values[key] is "True" or "False")values[key]=values[key].ToLowerInvariant();
        if(extra is not null)foreach(var (key,value) in extra)values[key]=value;
        return new(){Id=values.TryGetValue("id",out var recordId)&&int.TryParse(recordId,out var number)?number:I(json,"id"),Cell1=c1,Cell2=c2,Cell3=c3,Cell4=c4,Status=status,Details=details,Values=values};
    }
    private void Check(ClinicSession s)
    {
        if(_session is null||s.AccountId!=_session.AccountId||s.Role!=_session.Role||s.PatientId!=_session.PatientId||s.EmployeeId!=_session.EmployeeId)
            throw new InvalidOperationException("Сессия завершена. Войдите снова.");
    }
    private async Task<JsonElement> Request(string method,string path,object? body=null)
    {
        using var request=new HttpRequestMessage(new HttpMethod(method),path);
        if(body is not null)request.Content=body is HttpContent content?content:JsonContent.Create(body);
        HttpResponseMessage response;
        try{response=await _http.SendAsync(request);}catch(HttpRequestException){throw new InvalidOperationException("Не удалось подключиться к поликлинике. Проверьте сеть и повторите попытку.");}catch(TaskCanceledException){throw new InvalidOperationException("Сервис не ответил вовремя. Повторите попытку.");}
        using(response)
        {
            var text=await response.Content.ReadAsStringAsync();
            if(!response.IsSuccessStatusCode)
            {
                var message=response.StatusCode==HttpStatusCode.Unauthorized?"Сессия истекла или неверный пароль. Войдите снова.":"Не удалось выполнить действие. Повторите попытку.";
                try{using var document=JsonDocument.Parse(text);var error=document.RootElement;var title=S(error,"title");if(title.Length>0)message=title;}catch(JsonException){}
                throw new InvalidOperationException(message);
            }
            if(text.Length==0)return default;
            using var payload=JsonDocument.Parse(text);return payload.RootElement.Clone();
        }
    }
    private async Task<List<JsonElement>> Many(string path)
    {
        var rows=new List<JsonElement>();var page=1;
        while(true)
        {
            var json=await Request("GET",path+(path.Contains('?')?"&":"?")+"page="+page+"&pageSize=200");
            if(json.ValueKind==JsonValueKind.Array)return A(json);
            var items=A(P(json,"items"));rows.AddRange(items);
            if(items.Count==0||rows.Count>=I(json,"total"))return rows;
            if(++page>1000)throw new InvalidOperationException("Слишком большой список. Уточните фильтр.");
        }
    }
    public async Task<(ClinicSession Session,LiveDashboard Dashboard)?> SignInAsync(string login,string password)
    {
        _session=null;_http.DefaultRequestHeaders.Authorization=null;
        var result=await Request("POST","auth/login",new{login,password});var account=P(result,"userAccount");
        var role=S(account,"roleCode") switch{"patient"=>"Пациент","registrar"=>"Регистратор","doctor"=>"Врач","admin" or "administrator"=>"Администратор",_=>S(account,"roleName")};
        if(role is not ("Пациент" or "Регистратор" or "Врач" or "Администратор"))throw new InvalidOperationException("Эта роль не входит в приложение.");
        _http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",S(result,"token"));
        Permissions=A(P(result,"permissions")).Select(x=>x.ToString()).ToArray();
        _session=new(I(account,"id"),I(account,"employeeId")==0?null:I(account,"employeeId"),I(account,"patientId")==0?null:I(account,"patientId"),S(account,"login"),role);
        var person=role=="Пациент"?P(await Request("GET","api/my/profile"),"patient"):await Request("GET","api/employees/"+_session.EmployeeId);
        _session=_session with{Name=FullName(person)};
        return (_session,await DashboardAsync(_session));
    }
    public async Task<bool?> GetUserThemeAsync(int accountId)
    {
        if(_session?.AccountId!=accountId)throw new InvalidOperationException("Нет активной сессии.");
        return S(P(await Request("GET","api/me"),"account"),"userTheme")=="dark";
    }
    public async Task SaveUserThemeAsync(int accountId,bool dark)
    {
        if(_session?.AccountId!=accountId)throw new InvalidOperationException("Нет активной сессии.");
        await Request("PUT","api/me/theme",new{theme=dark?"dark":"light"});
    }
    public async Task ChangePasswordAsync(string oldPassword,string newPassword)=>_ = await Request("PUT","api/me/password",new{oldPassword,newPassword});
    public async Task<LiveDashboard> DashboardAsync(ClinicSession s)
    {
        Check(s);var rows=await ListAsync(s,s.Role=="Администратор"?"accounts":"appointments");
        DashboardMetric Metric(int count,string name,string detail)=>new(count.ToString(),name,detail,"#E4F2FF");
        DashboardMetric[] metrics;
        if(s.Role=="Пациент")metrics=[Metric(rows.Count(r=>r.Status=="запланирована"),"Мои записи","Запланированы"),Metric((await ListAsync(s,"visits")).Count,"Посещения","История"),Metric((await ListAsync(s,"referrals")).Count(r=>r.Status=="выполнено"),"Результаты","Готовы")];
        else if(s.Role=="Администратор")metrics=[Metric(rows.Count(r=>r.Status=="активен"),"Пользователи","Активные"),Metric((await Many("api/roles")).Count(r=>S(r,"code")!="lab"&&S(r,"code")!="lab-technician"),"Роли","В системе"),Metric((await ListAsync(s,"audit")).Count,"Аудит","События")];
        else metrics=[Metric(rows.Count(r=>r.Values.GetValueOrDefault("work_date")==DateTime.Today.ToString("yyyy-MM-dd")),"Приёмы сегодня","Расписание"),Metric(rows.Count(r=>r.Status=="запланирована"),"Запланированы","Записи"),Metric((await ListAsync(s,"patients")).Count,"Пациенты","Доступные карты")];
        return new(metrics,rows.Take(5).Select(r=>new DashboardItem(r.Cell1,r.Cell2,r.Cell3,r.Status)).ToArray(),s.Role=="Администратор"?"Пользователи":"Приёмы",Catalog.WorkspaceNote(s.Role));
    }
    private async Task<List<JsonElement>> Patients(ClinicSession s)
    {
        if(s.Role=="Пациент")return [P(await Request("GET","api/my/profile"),"patient")];
        if(s.Role!="Врач")return await Many("api/patients?includeInactive=true");
        var days=await Many("api/doctor/schedule?from="+DateTime.Today.AddYears(-1).ToString("yyyy-MM-dd")+"&to="+DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"));
        var ids=days.SelectMany(d=>A(P(d,"appointments"))).Select(a=>I(a,"patientId")).Distinct();var result=new List<JsonElement>();
        foreach(var id in ids)result.Add(P(await Request("GET","api/patients/"+id),"patient"));return result;
    }
    private async Task<List<JsonElement>> Visits(ClinicSession s)
    {
        if(s.Role=="Пациент")return A(P(await Request("GET","api/my/medcard"),"visits"));
        var summaries=await Many("api/visits");if(s.Role!="Врач")return summaries;
        var result=new List<JsonElement>();foreach(var v in summaries)result.Add(await Request("GET","api/visits/"+I(v,"id")));return result;
    }
    public async Task<List<Record>> ListAsync(ClinicSession s,string page,DateTime? from=null,DateTime? to=null)
    {
        Check(s);if(!Catalog.Sections(s.Role).Any(p=>p.Key==page))throw new InvalidOperationException("Раздел недоступен.");
        var rows=new List<Record>();
        switch(page)
        {
            case "patients":
                foreach(var p in await Patients(s))
                {
                    var profile=await Request("GET",s.Role=="Пациент"?"api/my/profile":"api/patients/"+I(p,"id"));
                    var policies=A(P(profile,"policies"));var oms=policies.FirstOrDefault(x=>S(x,"kind")=="ОМС"&&B(x,"isActive"));var dms=policies.FirstOrDefault(x=>S(x,"kind")=="ДМС"&&B(x,"isActive"));var attachment=P(profile,"attachment");
                    var extra=new Dictionary<string,string>{["oms_policy"]=S(oms,"number"),["oms_company_id"]=S(oms,"companyId"),["dms_policy"]=S(dms,"number"),["dms_company_id"]=S(dms,"companyId"),["district_id"]=S(attachment,"districtId"),["primary_doctor_id"]=S(attachment,"employeeId")};
                    rows.Add(Row(p,FullName(p),DisplayDate(S(p,"birthDate")),S(oms,"number"),S(p,"phone"),B(p,"isActive")?"активен":"архив","Карта: "+S(p,"cardNumber")+"\nАдрес: "+S(p,"address")+"\nСНИЛС: "+S(p,"snils")+"\nОМС: "+S(oms,"number")+" · "+S(oms,"companyName")+"\nДМС: "+S(dms,"number")+"\nУчасток: "+S(attachment,"districtNumber")+"\nУчастковый врач: "+S(attachment,"doctorName"),extra));
                }break;
            case "schedule":
                foreach(var v in await Many("api/schedules?from="+(from??DateTime.Today.AddDays(-30)).ToString("yyyy-MM-dd")+"&to="+(to??DateTime.Today.AddDays(30)).ToString("yyyy-MM-dd")+(s.Role=="Врач"?"&employeeId="+s.EmployeeId:"")))
                    rows.Add(Row(v,DisplayDate(S(v,"workDate")),S(v,"doctorName"),Time(v,"startTime")+" — "+Time(v,"endTime"),S(v,"roomNumber"),"слот "+S(v,"slotMinutes")+" мин",extra:new(){["start_time"]=Time(v,"startTime"),["end_time"]=Time(v,"endTime")}));break;
            case "slots":
                var doctors=(await Many("api/doctors")).ToDictionary(v=>I(v,"id"));
                var begin=from??DateTime.Today;var end=to??begin.AddDays(14);if(end<begin||(end-begin).TotalDays>31)throw new InvalidOperationException("Выберите период слотов не больше 31 дня.");
                for(var day=begin.Date;day<=end.Date;day=day.AddDays(1))foreach(var v in await Many("api/slots?date="+day.ToString("yyyy-MM-dd")))
                {
                    doctors.TryGetValue(I(v,"employeeId"),out var doctor);var key=I(v,"scheduleId")+"|"+Time(v,"startTime");
                    rows.Add(Row(v,DisplayDate(S(v,"workDate"))+" "+Time(v,"startTime"),S(v,"doctorName"),S(doctor,"specialtyName"),"", "свободно","Свободное время",new(){["id"]=(I(v,"scheduleId")*1440+(int)TimeSpan.Parse(Time(v,"startTime")).TotalMinutes).ToString(),["slot"]=key,["start_time"]=Time(v,"startTime")}));
                }break;
            case "appointments":
                if(s.Role=="Врач")
                {
                    foreach(var day in await Many("api/doctor/schedule?from="+(from??DateTime.Today.AddDays(-30)).ToString("yyyy-MM-dd")+"&to="+(to??DateTime.Today.AddDays(30)).ToString("yyyy-MM-dd")))foreach(var v in A(P(day,"appointments")))
                        rows.Add(Row(v,DisplayDate(S(day,"workDate"))+" "+Time(v,"startTime"),S(v,"patientName"),S(v,"cardNumber"),S(day,"roomNumber"),S(v,"status"),"Пациент: "+S(v,"patientName"),new(){["work_date"]=S(day,"workDate"),["schedule_id"]=S(day,"scheduleId")}));
                }
                else foreach(var v in await Many(s.Role=="Пациент"?"api/my/appointments":"api/appointments"))
                    rows.Add(Row(v,DisplayDate(S(v,"workDate"))+" "+Time(v,"startTime"),s.Role=="Пациент"?S(v,"doctorName"):S(v,"patientName"),s.Role=="Пациент"?S(v,"specialty"):S(v,"doctorName"),S(v,"room"),S(v,"status"),"Врач: "+S(v,"doctorName")+"\nИсточник: "+S(v,"source"),new(){["patient_id"]=(s.PatientId??I(v,"patientId")).ToString()}));break;
            case "visits":case "prescriptions":case "rendered":
                var names=(await Patients(s)).ToDictionary(p=>I(p,"id"),FullName);
                foreach(var visit in await Visits(s))
                {
                    var patient=I(visit,"patientId");var name=names.GetValueOrDefault(patient,S(visit,"patientName"));
                    if(page=="visits")rows.Add(Row(visit,DisplayDate(S(visit,"visitedAt")),name,S(visit,"doctorName"),S(visit,"diagnosisCode"),B(visit,"isClosed")?"закрыт":"в работе","Жалобы: "+S(visit,"complaints")+"\nАнамнез: "+S(visit,"anamnesis")+"\nОсмотр: "+S(visit,"objectiveStatus")+"\nДиагноз: "+S(visit,"diagnosisName")+"\nРекомендации: "+S(visit,"recommendations")));
                    if(page=="prescriptions")foreach(var v in A(P(visit,"prescriptions")))rows.Add(Row(v,S(v,"drugName"),name,S(v,"dosage"),S(visit,"doctorName"),B(v,"isRecipe")?"рецепт":"назначение",S(v,"instruction"),new(){["patient_id"]=patient.ToString(),["can_edit"]="false"}));
                    if(page=="rendered")foreach(var v in await Many("api/visits/"+I(visit,"id")+"/services"))rows.Add(Row(v,S(v,"serviceName"),name,DisplayDate(S(v,"renderedAt")),Money(v,"sum"),I(v,"invoiceId")==0?"без счёта":"счёт №"+S(v,"invoiceId"),"Количество: "+S(v,"quantity"),new(){["patient_id"]=patient.ToString(),["can_edit"]="false"}));
                }break;
            case "referrals":
                foreach(var v in await Many(s.Role=="Пациент"?"api/my/referrals":"api/referrals?status=all"))
                {
                    var files=I(v,"fileCount")>0?await Many(s.Role=="Пациент"?"api/my/referrals/"+I(v,"id")+"/files":"api/referrals/"+I(v,"id")+"/files"):[];var file=files.FirstOrDefault();
                    rows.Add(Row(v,S(v,"serviceName"),S(v,"patientName"),DisplayDate(S(v,"doneAt")),S(v,"executorName"),S(v,"status"),"Результат: "+S(v,"result")+"\nФайлы: "+string.Join(", ",files.Select(f=>S(f,"fileName"))),new(){["file_name"]=S(file,"fileName"),["file_id"]=S(file,"id")}));
                }break;
            case "invoices":
                foreach(var v in await Many(s.Role=="Пациент"?"api/my/invoices":"api/invoices"))
                {
                    var paid=S(v,"status")=="оплачен"?S(v,"total"):"0";
                    rows.Add(Row(v,"Счёт №"+I(v,"id"),S(v,"patientName"),Money(v,"total"),paid+" ₽",S(v,"status"),"Создан: "+DisplayDate(S(v,"createdAt"))+"\nСпособ оплаты: "+S(v,"methodName"),new(){["total_text"]=S(v,"total"),["paid"]=paid}));
                }break;
            case "documents":
                foreach(var v in await Many(s.Role=="Пациент"?"api/my/documents":"api/documents"))rows.Add(Row(v,S(v,"typeName")+" · "+S(v,"number"),S(v,"patientName"),DisplayDate(S(v,"issuedAt")),"", "оформлен",S(v,"content"),new(){["created_at"]=S(v,"issuedAt")}));break;
            case "notifications":
                foreach(var v in await Many("api/my/notifications"))rows.Add(Row(v,"Уведомление",DisplayDate(S(v,"createdAt")),S(v,"message"),"",B(v,"isRead")?"прочитано":"новое",S(v,"message")));break;
            case "accounts":
                foreach(var v in await Many("api/accounts"))if(S(v,"roleName")!="Лаборант")rows.Add(Row(v,S(v,"login"),S(v,"ownerName"),S(v,"roleName"),DisplayDate(S(v,"lastLogin")),B(v,"isActive")?"активен":"заблокирован", "Роль: "+S(v,"roleName")));break;
            case "employees":
                foreach(var v in await Many("api/employees?includeInactive=true"))rows.Add(Row(v,FullName(v),S(v,"postName"),S(v,"specialtyName"),S(v,"phone"),B(v,"isActive")?"активен":"архив"));break;
            case "services":
                foreach(var v in await Many("api/services?includeInactive=true"))rows.Add(Row(v,S(v,"name"),S(v,"groupName"),Money(v,"price"),"",B(v,"isActive")?"активна":"архив"));break;
            case "dictionary":
                foreach(var (table,route) in DictionaryRoutes)foreach(var v in await Many("api/"+route))rows.Add(Row(v,DictionaryLabel(table),table=="diagnosis"?S(v,"code"):S(v,"id"),table=="room"?S(v,"number"):S(v,"name"),"","справочник",extra:new(){["table"]=table,["name"]=table=="room"?S(v,"number"):S(v,"name"),["code"]=table=="room"?S(v,"floor"):S(v,"code")}));break;
            case "audit":
                foreach(var v in await Many("api/audits"))rows.Add(Row(v,S(v,"actedAt"),S(v,"login"),S(v,"action"),S(v,"tableName")+" №"+S(v,"recordId"),"аудит","Пациент: "+S(v,"patientId")));break;
            case "reports":
                var range="?from="+(from??DateTime.Today.AddDays(-30)).ToString("yyyy-MM-dd")+"&to="+(to??DateTime.Today).ToString("yyyy-MM-dd");var id=0;
                foreach(var kind in new[]{"attendance","load","morbidity","revenue"})foreach(var v in await Many("api/reports/"+kind+range))
                {
                    var c1=kind switch{"attendance"=>"Посещаемость: "+DisplayDate(S(v,"date")),"load"=>"Загрузка: "+S(v,"doctorName"),"morbidity"=>"Диагноз: "+S(v,"diagnosisCode"),_=>"Выручка: "+DisplayDate(S(v,"date"))};
                    var c2=kind switch{"attendance"=>S(v,"total"),"load"=>S(v,"loadPercent")+" %","morbidity"=>S(v,"visits"),_=>Money(v,"amount")};
                    rows.Add(Row(v,c1,c2,DisplayDate((from??DateTime.Today.AddDays(-30)).ToString("yyyy-MM-dd"))+" — "+DisplayDate((to??DateTime.Today).ToString("yyyy-MM-dd")),"","отчёт",v.ToString(),new(){["id"]=(++id).ToString()}));
                }break;
        }
        return rows;
    }
    private static readonly Dictionary<string,string> DictionaryRoutes=new(){["specialty"]="specialties",["post"]="posts",["room"]="rooms",["service_group"]="servicegroups",["drug"]="drugs",["diagnosis"]="diagnoses",["district"]="districts",["insurance_company"]="insurancecompanies",["document_type"]="documenttypes",["pay_method"]="paymethods"};
    private static string DictionaryLabel(string key)=>key switch{"specialty"=>"Специальности","post"=>"Должности","room"=>"Кабинеты","service_group"=>"Категории услуг","drug"=>"Лекарства","diagnosis"=>"МКБ-10","district"=>"Участки","insurance_company"=>"Страховые", "document_type"=>"Документы",_=>"Способы оплаты"};
}
