using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Clinic.Workspace;

namespace Clinic.Data;

public sealed partial class HttpClinicGateway
{
    private int _editorId;
    private string _editorKind="";
    private FieldSpec[] _permissionFields=[];
    public void PrepareEditor(string kind,int id){_editorKind=kind;_editorId=id;}
    public FieldSpec[] FieldsFor(string kind,int id=0)=>kind switch
    {
        "role_permissions"=>_permissionFields,
        "rendered" when id>0=>Catalog.Fields(kind).Where(f=>f.Key!="visit_id").ToArray(),
        "patient"=>Catalog.Fields("patient").Where(f=>new[]{"last_name","first_name","middle_name","birth_date","gender","oms_policy","snils","dms_policy","phone","address","primary_doctor_id"}.Contains(f.Key)).Concat([new("oms_company_id","Страховая ОМС",true,"companies"),new("dms_company_id","Страховая ДМС",Lookup:"companies"),new("district_id","Участок",Lookup:"districts")]).ToArray(),
        "prescription"=>Catalog.Fields(kind).Where(f=>f.Key!="valid_until" && (id==0||f.Key!="visit_id")).ToArray(),
        "referral"=>Catalog.Fields(kind).Where(f=>f.Key!="consultation_doctor_id").ToArray(),
        "payment"=>[new("method_id","Способ оплаты (полная сумма счёта)",true,"methods"),new("policy_id","Полис, только при оплате по ДМС",Lookup:"policies")],
        "document"=>[new("patient_id","Пациент",true,"patients"),new("type_id","Вид документа",true,"document_types"),new("visit_id","Приём (обязательно для справки врача)",Lookup:"visits"),new("content","Текст документа",true,Multiline:true)],
        "account" when id==0=>Catalog.Fields(kind).Select(f=>f.Key=="password"?f with { Label="Пароль (от 8 символов)", Required=true }:f).ToArray(),
        "account" when id>0=>[new("role_id","Роль",true,"roles"),new("password","Новый пароль (пусто — оставить текущий)",Secret:true)],
        "registration"=>[new("last_name","Фамилия",true),new("first_name","Имя",true),new("middle_name","Отчество"),new("birth_date","Дата рождения (ГГГГ-ММ-ДД)",true,Default:null),new("gender","Пол",true,"gender"),new("snils","СНИЛС"),new("phone","Телефон"),new("address","Адрес"),new("oms_policy","Полис ОМС",true),new("oms_company_id","Страховая ОМС",true,"companies"),new("login","Логин",true),new("password","Пароль (от 8 символов)",true,Secret:true),new("password_confirmation","Подтверждение пароля",true,Secret:true)],
        _=>Catalog.Fields(kind)
    };
    public async Task<Dictionary<string,List<Choice>>> RegistrationChoicesAsync()=>new(){["gender"]=[new("М","Мужской"),new("Ж","Женский")],["companies"]=(await Many("auth/insurancecompanies")).Select(c=>new Choice(S(c,"id"),S(c,"name"))).ToList()};
    public async Task<Dictionary<string,List<Choice>>> ChoicesAsync(ClinicSession s)
    {
        Check(s);var result=new Dictionary<string,List<Choice>>();
        foreach(var (key,route) in new[]{("doctors","doctors"),("rooms","rooms"),("diagnoses","diagnoses"),("drugs","drugs"),("services","services"),("posts","posts"),("specialties","specialties"),("groups","servicegroups"),("methods","paymethods"),("companies","insurancecompanies"),("districts","districts"),("document_types","documenttypes")})
            result[key]=(await Many("api/"+route)).Select(v=>new Choice(route=="diagnoses"?S(v,"code"):S(v,"id"),route=="rooms"?S(v,"number"):route=="doctors"?S(v,"fullName"):route=="diagnoses"?S(v,"code")+" · "+S(v,"name"):S(v,"name"))).ToList();
        if(s.Role=="Администратор")
        {
            result["roles"]=(await Many("api/roles")).Where(v=>new[]{"patient","registrar","doctor","admin","administrator"}.Contains(S(v,"code"))).Select(v=>new Choice(S(v,"id"),S(v,"name"))).ToList();
            if(_editorKind=="role_permissions")
            {
                var role=(await Many("api/roles")).First(r=>I(r,"id")==_editorId);
                var enabled=A(P(role,"permissions")).Select(p=>p.ToString()).ToHashSet();
                _permissionFields=(await Many("api/permissions")).Select(p=>new FieldSpec("perm:"+S(p,"code"),Catalog.PermissionName(S(p,"code")),true,"boolean",Default:enabled.Contains(S(p,"code"))?"true":"false")).ToArray();
            }
            result["employees"]=(await Many("api/employees")).Select(v=>new Choice(S(v,"id"),FullName(v))).ToList();
        }
        if(s.Role!="Администратор"||_editorKind=="account")result["patients"]=(await Patients(s)).Where(p=>B(p,"isActive")).Select(p=>new Choice(S(p,"id"),FullName(p))).ToList();
        if(s.Role is "Врач" or "Регистратор")
        {
            var visits=await Visits(s);result["visits"]=visits.Select(v=>new Choice(S(v,"id"),(S(v,"patientName").Length>0?S(v,"patientName"):"Пациент №"+S(v,"patientId"))+" · "+DisplayDate(S(v,"visitedAt")))).ToList();
            result["open_visits"]=result["visits"].Where(c=>!B(visits.First(v=>I(v,"id").ToString()==c.Id),"isClosed")).ToList();
        }
        if(s.Role is "Пациент" or "Регистратор")result["slots"]=(await ListAsync(s,"slots")).Select(r=>new Choice(r.Values["slot"],r.Cell1+" · "+r.Cell2)).ToList();
        if(_editorKind=="payment")
        {
            var invoice=P(await Request("GET","api/invoices/"+_editorId),"invoice");var profile=await Request("GET","api/patients/"+I(invoice,"patientId"));
            result["policies"]=A(P(profile,"policies")).Where(p=>S(p,"kind")=="ДМС"&&B(p,"isActive")).Select(p=>new Choice(S(p,"id"),S(p,"number")+" · "+S(p,"companyName"))).ToList();
        }
        result["gender"]=[new("М","Мужской"),new("Ж","Женский")];result["boolean"]=[new("false","Нет"),new("true","Да")];
        result["dictionary_types"]=DictionaryRoutes.Keys.Select(t=>new Choice(t,DictionaryLabel(t))).ToList();return result;
    }
    private static string V(Dictionary<string,string> values,string key)=>values.GetValueOrDefault(key,"").Trim();
    private static int Number(Dictionary<string,string> values,string key)=>int.TryParse(V(values,key),out var n)?n:throw new InvalidOperationException("Заполните поле: "+key);
    private static int? Optional(Dictionary<string,string> values,string key)=>V(values,key)==""?null:Number(values,key);
    private static string? Nullable(Dictionary<string,string> values,string key)=>V(values,key)==""?null:V(values,key);
    private static object Person(Dictionary<string,string> v)=>new{lastName=V(v,"last_name"),firstName=V(v,"first_name"),middleName=Nullable(v,"middle_name"),birthDate=V(v,"birth_date"),gender=V(v,"gender"),snils=Nullable(v,"snils"),address=Nullable(v,"address"),phone=Nullable(v,"phone")};
    public async Task<int> SaveAsync(ClinicSession s,Mutation m)
    {
        Check(s);var v=m.Values;var id=m.Id;JsonElement result;
        foreach(var f in FieldsFor(m.Kind,id))if(f.Required&&V(v,f.Key)=="")throw new InvalidOperationException("Заполните «"+f.Label+"».");
        switch(m.Kind)
        {
            case "role_permissions":
                result=await Request("PUT","api/roles/"+id+"/permissions",new{codes=v.Where(p=>p.Key.StartsWith("perm:")&&p.Value=="true").Select(p=>p.Key[5..]).ToArray()});return id;
            case "password":
                if(v.GetValueOrDefault("new_password","")!=v.GetValueOrDefault("confirmation",""))throw new InvalidOperationException("Пароли не совпадают.");
                await ChangePasswordAsync(v["old_password"],v["new_password"]);return s.AccountId;
            case "patient":
                result=await Request(id==0?"POST":"PUT","api/patients"+(id==0?"/details":"/"+id+"/details"),new {
                    patient=Person(v),omsNumber=V(v,"oms_policy"),omsCompanyId=Number(v,"oms_company_id"),dmsNumber=Nullable(v,"dms_policy"),
                    dmsCompanyId=Optional(v,"dms_company_id"),districtId=Optional(v,"district_id"),employeeId=Optional(v,"primary_doctor_id") });break;
            case "schedule":result=await Request(id==0?"POST":"PUT","api/schedules"+(id==0?"":"/"+id),new{employeeId=Number(v,"employee_id"),roomId=Number(v,"room_id"),workDate=V(v,"work_date"),startTime=V(v,"start_time"),endTime=V(v,"end_time"),slotMinutes=Number(v,"slot_minutes")});break;
            case "booking":
                var slot=V(v,"slot").Split('|');if(slot.Length!=2)throw new InvalidOperationException("Выберите слот.");
                result=id==0?await Request("POST","api/appointments",new{scheduleId=int.Parse(slot[0]),startTime=slot[1],patientId=s.Role=="Пациент"?s.PatientId:Optional(v,"patient_id"),source=s.Role=="Пациент"?"приложение":"регистратура"}):await Request("POST","api/appointments/"+id+"/reschedule",new{scheduleId=int.Parse(slot[0]),startTime=slot[1]});break;
            case "visit":result=await Request("PUT","api/visits/"+id,new{complaints=V(v,"complaints"),anamnesis=Nullable(v,"anamnesis"),objectiveStatus=Nullable(v,"objective_status"),diagnosisCode=Nullable(v,"diagnosis_code"),recommendations=Nullable(v,"recommendations")});break;
            case "prescription":
                result=await Request(id==0?"POST":"PUT",id==0?"api/visits/"+Number(v,"visit_id")+"/prescriptions":"api/prescriptions/"+id,new{drugId=Number(v,"drug_id"),dosage=V(v,"dosage"),instruction=V(v,"instruction"),isRecipe=V(v,"is_recipe")=="true"});break;
            case "referral":result=await Request("POST","api/visits/"+Number(v,"visit_id")+"/referrals",new{serviceId=Number(v,"service_id")});break;
            case "result":
                using(var form=new MultipartFormDataContent())
                {
                    form.Add(new StringContent(V(v,"result")),"result");
                    var uploads=v.TryGetValue("files_json",out var filesJson)?JsonSerializer.Deserialize<List<AttachmentUpload>>(filesJson)??[]:
                        v.TryGetValue("file_data",out var data)&&data.Length>0?[new AttachmentUpload(V(v,"file_name"),data)]:[];
                    foreach(var upload in uploads)
                    {
                        var bytes=Convert.FromBase64String(upload.Data);if(bytes.Length>10*1024*1024)throw new InvalidOperationException("Максимум 10 МБ.");
                        var name=upload.Name;var mime=System.IO.Path.GetExtension(name).ToLowerInvariant() switch{".pdf"=>"application/pdf",".png"=>"image/png",".jpg" or ".jpeg"=>"image/jpeg",_=>throw new InvalidOperationException("Выберите файл PDF, JPG или PNG.")};
                        var content=new ByteArrayContent(bytes);content.Headers.ContentType=new MediaTypeHeaderValue(mime);form.Add(content,"files",name);
                    }
                    result=await Request("POST","api/referrals/"+id+"/complete",form);
                }break;
            case "rendered":
                result=await Request(id==0?"POST":"PUT",id==0?"api/visits/"+Number(v,"visit_id")+"/services":"api/visit-services/"+id,new{serviceId=Number(v,"service_id"),quantity=Number(v,"quantity")});break;
            case "payment":result=await Request("POST","api/invoices/"+id+"/pay",new{methodId=Number(v,"method_id"),policyId=Optional(v,"policy_id")});return id;
            case "document":result=await Request("POST","api/patients/"+Number(v,"patient_id")+"/documents",new{typeId=Number(v,"type_id"),visitId=Optional(v,"visit_id"),content=V(v,"content")});break;
            case "account":
                if(id==0)result=await Request("POST","api/accounts",new{login=V(v,"login"),password=v.GetValueOrDefault("password",""),roleId=Number(v,"role_id"),employeeId=Optional(v,"employee_id"),patientId=Optional(v,"patient_id")});
                else
                {
                    var old=(await Many("api/accounts")).First(a=>I(a,"id")==id);result=await Request("PUT","api/accounts/"+id,new{roleId=Number(v,"role_id"),isActive=B(old,"isActive")});
                    if(v.GetValueOrDefault("password","").Length>0)await Request("POST","api/accounts/"+id+"/reset-password",new{newPassword=v["password"]});
                }break;
            case "employee":case "service":
                var active=id==0||(m.Kind=="employee"&&B(await Request("GET","api/employees/"+id),"isActive"));
                // Services have a list route, not a single-item GET.
                if(m.Kind=="service"&&id>0)active=B((await Many("api/services?includeInactive=true")).First(x=>I(x,"id")==id),"isActive");
                result=await Request(id==0?"POST":"PUT","api/"+(m.Kind=="employee"?"employees":"services")+(id==0?"":"/"+id),m.Kind=="employee"?new{lastName=V(v,"last_name"),firstName=V(v,"first_name"),middleName=Nullable(v,"middle_name"),postId=Number(v,"post_id"),specialtyId=Optional(v,"specialty_id"),phone=Nullable(v,"phone"),isActive=active}:(object)new{name=V(v,"name"),groupId=Number(v,"group_id"),price=decimal.Parse(V(v,"price").Replace(',','.'),CultureInfo.InvariantCulture),isActive=active});break;
            case "dictionary":
                var table=V(v,"table");if(!DictionaryRoutes.TryGetValue(table,out var route))throw new InvalidOperationException("Неизвестный справочник.");
                object body=table switch{"room"=>new{number=V(v,"name"),floor=short.TryParse(V(v,"code"),out var floor)?floor:(short)1},"diagnosis"=>new{code=V(v,"code"),name=V(v,"name")},"district"=>new{number=V(v,"code"),name=V(v,"name")},"insurance_company"=>new{name=V(v,"name"),phone=(string?)null,isActive=true},"pay_method"=>new{name=V(v,"name"),isInsurance=V(v,"name")=="ДМС"},_=>new{name=V(v,"name")}};
                if(table=="diagnosis"&&id==0&&(await Many("api/diagnoses")).Any(x=>S(x,"code")==V(v,"code")))id=-1;
                result=await Request(id==0?"POST":"PUT","api/"+route+(id==0?"":"/"+Uri.EscapeDataString(table=="diagnosis"?V(v,"code"):id.ToString())),body);break;
            default:throw new InvalidOperationException("Операция недоступна.");
        }
        return I(result,"id") is var created&&created>0?created:id;
    }
    public async Task ActAsync(ClinicSession s,string action,int id,Dictionary<string,string>? values=null)
    {
        Check(s);
        switch(action)
        {
            case "delete_referral":await Request("DELETE","api/referrals/"+id);return;
            case "delete_schedule":await Request("DELETE","api/schedules/"+id);return;
            case "delete_prescription":await Request("DELETE","api/prescriptions/"+id);return;
            case "delete_rendered":await Request("DELETE","api/visit-services/"+id);return;
            case "delete_account":await Request("DELETE","api/accounts/"+id);return;
            case "cancel_invoice":await Request("POST","api/invoices/"+id+"/cancel");return;
            case "read_all":await Request("POST","api/my/notifications/read-all");return;
            case "delete_dictionary":
                var table=values?.GetValueOrDefault("table")??"";
                if(!DictionaryRoutes.TryGetValue(table,out var route))throw new InvalidOperationException("Неизвестный справочник");
                var code=table=="diagnosis"?values!.GetValueOrDefault("code",""):id.ToString();
                await Request("DELETE","api/"+route+"/"+Uri.EscapeDataString(code));return;

            case "start_visit":
                if((await Visits(s)).Any(v=>I(v,"appointmentId")==id))return;
                await Request("POST","api/visits",new{appointmentId=id});return;
            case "close_visit":await Request("POST","api/visits/"+id+"/close");return;
            case "cancel":case "no_show":await Request("POST","api/appointments/"+id+(action=="cancel"?"/cancel":"/no-show"));return;
            case "cancel_referral":await Request("POST","api/referrals/"+id+"/cancel");return;
            case "mark_read":await Request("POST","api/my/notifications/"+id+"/read");return;
            case "create_invoice":
                var service=(await ListAsync(s,"rendered")).First(r=>r.Id==id);await Request("POST","api/invoices",new{patientId=int.Parse(service.Values["patient_id"]),visitServiceIds=new[]{id}});return;
            case "toggle_patient":
                var patient=P(await Request("GET","api/patients/"+id),"patient");await Request(B(patient,"isActive")?"DELETE":"POST","api/patients/"+id+(B(patient,"isActive")?"":"/restore"));return;
            case "toggle_account":
                var account=(await Many("api/accounts")).First(x=>I(x,"id")==id);await Request("PUT","api/accounts/"+id,new{roleId=I(account,"roleId"),isActive=!B(account,"isActive")});return;
            case "toggle_employee":case "toggle_service":
                var key=action=="toggle_employee"?"employees":"services";var row=(await Many("api/"+key+"?includeInactive=true")).First(x=>I(x,"id")==id);
                if(B(row,"isActive"))await Request("DELETE","api/"+key+"/"+id);
                else await Request("PUT","api/"+key+"/"+id,key=="employees"?new{lastName=S(row,"lastName"),firstName=S(row,"firstName"),middleName=NullableString(row,"middleName"),postId=I(row,"postId"),specialtyId=I(row,"specialtyId")==0?(int?)null:I(row,"specialtyId"),phone=NullableString(row,"phone"),isActive=true}:(object)new{name=S(row,"name"),groupId=I(row,"groupId"),price=decimal.Parse(S(row,"price"),CultureInfo.InvariantCulture),isActive=true});return;
            default:throw new InvalidOperationException("Действие недоступно.");
        }
    }
    private static string? NullableString(JsonElement row,string key)=>S(row,key).Length==0?null:S(row,key);
    public async Task RegisterAsync(Dictionary<string,string> v)
    {
        if(v.GetValueOrDefault("password","")!=v.GetValueOrDefault("password_confirmation",""))throw new InvalidOperationException("Пароли не совпадают.");
        await Request("POST","auth/register",new{login=V(v,"login"),password=v.GetValueOrDefault("password",""),lastName=V(v,"last_name"),firstName=V(v,"first_name"),middleName=Nullable(v,"middle_name"),birthDate=V(v,"birth_date"),gender=V(v,"gender"),snils=Nullable(v,"snils"),address=Nullable(v,"address"),phone=Nullable(v,"phone"),omsNumber=V(v,"oms_policy"),omsCompanyId=Number(v,"oms_company_id")});
    }
    public async Task<List<AttachmentInfo>> AttachmentsAsync(ClinicSession s,int referralId)
    {
        Check(s);var files=await Many(s.Role=="Пациент"?"api/my/referrals/"+referralId+"/files":"api/referrals/"+referralId+"/files");
        return files.Select(f=>new AttachmentInfo(I(f,"id"),S(f,"fileName"),long.TryParse(S(f,"size"),out var size)?size:0)).ToList();
    }
    public async Task<byte[]> DownloadAttachmentAsync(ClinicSession s,int fileId)
    {
        Check(s);
        using var response=await _http.GetAsync((s.Role=="Пациент"?"api/my/referral-files/":"api/referral-files/")+fileId);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Не удалось скачать файл. Обновите список.");
        return await response.Content.ReadAsByteArrayAsync();
    }
    public async Task<byte[]?> AttachmentAsync(ClinicSession s,int referralId)
    {
        var files=await AttachmentsAsync(s,referralId);return files.Count==0?null:await DownloadAttachmentAsync(s,files[0].Id);
    }
    public async Task DeleteAttachmentAsync(ClinicSession s,int id) { Check(s);await Request("DELETE","api/referral-files/"+id); }
    public async Task<string> BackupAsync(ClinicSession s)
    {
        Check(s);var backup=await Request("POST","api/backups");return JsonSerializer.Serialize(new{kind="clinic-server-backup",fileName=S(backup,"fileName"),server=_http.BaseAddress!.ToString(),createdAt=S(backup,"createdAt")},new JsonSerializerOptions{WriteIndented=true});
    }
    public async Task RestoreAsync(ClinicSession s,string json)
    {
        Check(s);var marker=JsonDocument.Parse(json).RootElement;
        if(S(marker,"kind")!="clinic-server-backup"||S(marker,"server")!=_http.BaseAddress!.ToString())throw new InvalidOperationException("Эта копия относится к другой системе. Выберите копию текущей поликлиники.");
        await Request("POST","api/backups/restore",new{fileName=S(marker,"fileName")});
        SessionExpired?.Invoke("Данные восстановлены. Войдите снова.");
    }
}
