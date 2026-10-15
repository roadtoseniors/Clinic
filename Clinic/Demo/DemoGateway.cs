using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Clinic.Data;
using Clinic.Workspace;

namespace Clinic.Demo;

// Fictional, independent data. The browser never receives a database connection or production records.
public sealed class DemoGateway : IClinicGateway
{
    public static DemoGateway Instance { get; }=new();
    private Dictionary<string,List<Dictionary<string,string>>> _tables=new();
    private readonly Dictionary<int,string> _passwords=new();
    private static readonly CultureInfo Ru=CultureInfo.GetCultureInfo("ru-RU");
    private List<Dictionary<string,string>> T(string table) {if(!_tables.TryGetValue(table,out var list))_tables[table]=list=[];return list;}
    private Dictionary<string,string> Find(string table,int id)=>T(table).FirstOrDefault(r=>I(r,"id")==id)??throw new InvalidOperationException("Запись не найдена.");
    private static string S(Dictionary<string,string> r,string key)=>r.GetValueOrDefault(key,"");
    private static int I(Dictionary<string,string> r,string key)=>int.TryParse(S(r,key),out var n)?n:0;
    private static decimal N(Dictionary<string,string> r,string key)=>decimal.TryParse(S(r,key),NumberStyles.Number,CultureInfo.InvariantCulture,out var n)?n:0;
    private static string Name(Dictionary<string,string> r)=>string.Join(' ',new[]{S(r,"last_name"),S(r,"first_name"),S(r,"middle_name")}.Where(s=>s.Length>0));
    private int Put(string table,Dictionary<string,string> values,int id=0)
    {
        if(id==0) {id=T(table).Select(r=>I(r,"id")).DefaultIfEmpty(0).Max()+1;var row=new Dictionary<string,string>(values){["id"]=id.ToString()};T(table).Add(row);}
        else foreach(var (k,v) in values)Find(table,id)[k]=v;
        return id;
    }
    private void Seed(string table,params (string,string)[] values)=>Put(table,values.ToDictionary(v=>v.Item1,v=>v.Item2));
    public DemoGateway()
    {
        foreach(var n in new[]{"Терапия","Кардиология","Неврология"})Seed("specialty",("name",n));
        foreach(var n in new[]{"Врач","Регистратор","Администратор"})Seed("post",("name",n));
        foreach(var n in new[]{"Пациент","Регистратор","Врач","Администратор"})Seed("role",("name",n));
        foreach(var n in new[]{"Приём","Анализы","Диагностика"})Seed("service_group",("name",n));
        foreach(var n in new[]{"Наличные","Банковская карта","СБП","ДМС"})Seed("pay_method",("name",n));
        for(var i=1;i<=3;i++)Seed("room",("number","10"+i),("floor","1"));
        foreach(var (code,name) in new[]{("J06.9","ОРВИ"),("I10","Первичная гипертензия"),("G43.9","Мигрень")})Seed("diagnosis",("code",code),("name",name));
        foreach(var n in new[]{"Парацетамол","Лозартан","Омепразол"})Seed("drug",("name",n));
        for(var i=1;i<=4;i++)Seed("service",("name",new[]{"Приём терапевта","Общий анализ крови","ЭКГ","Консультация кардиолога"}[i-1]),("group_id",i is 1 or 4?"1":i==2?"2":"3"),("price",new[]{"1800","450","900","2500"}[i-1]),("is_active","true"));
        Seed("employee",("last_name","Соколов"),("first_name","Андрей"),("middle_name","Викторович"),("post_id","1"),("specialty_id","1"),("phone","+7 (000) 000-01-01"),("is_active","true"));
        Seed("employee",("last_name","Иванова"),("first_name","Елена"),("post_id","1"),("specialty_id","2"),("is_active","true"));
        Seed("employee",("last_name","Петрова"),("first_name","Анна"),("post_id","2"),("is_active","true"));
        Seed("employee",("last_name","Орлов"),("first_name","Сергей"),("post_id","3"),("is_active","true"));
        for(var i=1;i<=4;i++)Seed("patient",("last_name",new[]{"Кириллов","Захарова","Никитин","Белова"}[i-1]),("first_name",new[]{"Максим","Ирина","Павел","Софья"}[i-1]),("birth_date",new[]{"1985-03-14","1992-07-21","1978-11-02","1995-09-17"}[i-1]),("gender",i%2==0?"Ж":"М"),("card_number","ДЕМО-00"+i),("oms_policy","000000000000000"+i),("dms_policy",i==1?"ДЕМО-ДМС-01":""),("phone","+7 (000) 000-00-0"+i),("address","Демонстрационный адрес"),("is_active","true"));
        Seed("account",("login","kirillov"),("role_id","1"),("patient_id","1"),("is_active","true"));
        Seed("account",("login","petrova"),("role_id","2"),("employee_id","3"),("is_active","true"));
        Seed("account",("login","sokolov"),("role_id","3"),("employee_id","1"),("is_active","true"));
        Seed("account",("login","admin"),("role_id","4"),("employee_id","4"),("is_active","true"));
        foreach(var a in T("account"))_passwords[I(a,"id")]="demo123";
        for(var d=-2;d<8;d++)for(var doc=1;doc<=2;doc++)Seed("schedule",("employee_id",doc.ToString()),("room_id",doc.ToString()),("work_date",DateTime.Today.AddDays(d).ToString("yyyy-MM-dd")),("start_time","09:00"),("end_time","17:00"),("slot_minutes","30"));
        var today=T("schedule").First(r=>I(r,"employee_id")==1&&S(r,"work_date")==DateTime.Today.ToString("yyyy-MM-dd"));
        for(var i=1;i<=3;i++)Seed("appointment",("schedule_id",S(today,"id")),("patient_id",i.ToString()),("start_time",new[]{"09:00","09:30","10:00"}[i-1]),("status","запланирована"),("source","регистратура"));
        Seed("appointment",("schedule_id","1"),("patient_id","1"),("start_time","09:00"),("status","завершена"),("source","приложение"));
        Seed("visit",("appointment_id","4"),("visited_at",DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd")),("complaints","Плановый профилактический осмотр"),("anamnesis","Демонстрационный протокол"),("objective_status","Без особенностей"),("diagnosis_code","J06.9"),("recommendations","Повторный осмотр при необходимости"),("is_closed","true"));
        Seed("referral",("visit_id","1"),("service_id","2"),("status","выполнено"),("executor_id","1"),("result","Демонстрационный результат: показатели в пределах нормы."));
        Seed("prescription",("visit_id","1"),("drug_id","1"),("dosage","Демо"),("instruction","Пример записи назначения, не медицинская рекомендация."),("is_recipe","false"));
        Seed("visit_service",("visit_id","1"),("service_id","1"),("quantity","1"),("price","1800"),("rendered_at",DateTime.Today.ToString("yyyy-MM-dd")));
        Seed("notification",("patient_id","1"),("title","Добро пожаловать"),("message","Это независимый демонстрационный набор данных."),("created_at",DateTime.Now.ToString("s")),("is_read","false"));
    }
    private void Check(ClinicSession s,params string[] roles)
    {
        var a=Find("account",s.AccountId);var role=S(Find("role",I(a,"role_id")),"name");
        if(S(a,"is_active")!="true"||role!=s.Role||I(a,"patient_id")!=(s.PatientId??0)||I(a,"employee_id")!=(s.EmployeeId??0)||(roles.Length>0&&!roles.Contains(role)))throw new InvalidOperationException("Недостаточно прав.");
    }
    private bool Visible(ClinicSession s,Dictionary<string,string> a)=>s.Role=="Пациент"?I(a,"patient_id")==s.PatientId:s.Role=="Врач"?I(Find("schedule",I(a,"schedule_id")),"employee_id")==s.EmployeeId:s.Role=="Регистратор";
    private Dictionary<string,string> Appointment(Dictionary<string,string> v)=>Find("appointment",I(v,"appointment_id"));
    private bool VisitVisible(ClinicSession s,Dictionary<string,string> v)=>Visible(s,Appointment(v));
    private int OwnVisit(ClinicSession s,int id,bool closed=false)
    {
        Check(s,"Врач");var v=Find("visit",id);if(!VisitVisible(s,v)||(!closed&&S(v,"is_closed")=="true"))throw new InvalidOperationException("Приём закрыт или принадлежит другому врачу.");return I(Appointment(v),"patient_id");
    }
    private void Notify(int p,string title,string text)=>Seed("notification",("patient_id",p.ToString()),("title",title),("message",text),("created_at",DateTime.Now.ToString("s")),("is_read","false"));
    private void Audit(ClinicSession s,string table)=>Seed("audit",("account_id",s.AccountId.ToString()),("acted_at",DateTime.Now.ToString("s")),("action","UPDATE"),("table_name",table));
    public async Task<(ClinicSession Session,LiveDashboard Dashboard)?> SignInAsync(string login,string password)
    {
        login=login.Trim().ToLowerInvariant();login=login switch {"patient"=>"kirillov","registrar"=>"petrova","doctor"=>"sokolov",_=>login};
        var a=T("account").FirstOrDefault(a=>S(a,"login").Equals(login,StringComparison.OrdinalIgnoreCase)&&S(a,"is_active")=="true");
        if(a is null||_passwords.GetValueOrDefault(I(a,"id"))!=password)return null;
        var person=I(a,"patient_id")>0?Find("patient",I(a,"patient_id")):Find("employee",I(a,"employee_id"));
        if(S(person,"is_active")!="true")return null;
        var s=new ClinicSession(I(a,"id"),I(a,"employee_id")==0?null:I(a,"employee_id"),I(a,"patient_id")==0?null:I(a,"patient_id"),Name(person),S(Find("role",I(a,"role_id")),"name"));
        a["last_login"]=DateTime.Now.ToString("s");return(s,await DashboardAsync(s));
    }
    public Task<bool?> GetUserThemeAsync(int id)=>Task.FromResult<bool?>(T("user_theme").FirstOrDefault(t=>I(t,"account_id")==id) is { } row?S(row,"theme")=="dark":null);
    public Task SaveUserThemeAsync(int id,bool dark)
    {
        var theme=T("user_theme").FirstOrDefault(t=>I(t,"account_id")==id);if(theme is null)Seed("user_theme",("account_id",id.ToString()),("theme",dark?"dark":"light"));else theme["theme"]=dark?"dark":"light";return Task.CompletedTask;
    }
    public async Task<LiveDashboard> DashboardAsync(ClinicSession s)
    {
        Check(s);
        DashboardMetric Metric(int count,string label,string detail,string color)=>new(count.ToString(),label,detail,color);
        bool Upcoming(Dictionary<string,string> a)=>S(a,"status")=="запланирована"&&DateTime.Parse(S(Find("schedule",I(a,"schedule_id")),"work_date"))+TimeSpan.Parse(S(a,"start_time"))>=DateTime.Now;
        bool Today(Dictionary<string,string> a)=>DateTime.Parse(S(Find("schedule",I(a,"schedule_id")),"work_date")).Date==DateTime.Today;
        var appointments=T("appointment").Where(a=>Visible(s,a)).ToArray();
        DashboardMetric[] metrics;List<Record> rows;string title;
        if(s.Role=="Администратор")
        {
            metrics=[Metric(T("account").Count(a=>S(a,"is_active")=="true"),"Активные пользователи","Учётные записи","#E4F2FF"),Metric(4,"Роли","В приложении","#E1F6ED"),Metric(T("audit").Count,"События аудита","Журнал изменений","#FFF2DF")];
            rows=(await ListAsync(s,"audit")).Take(5).ToList();title="Последние события";
        }
        else if(s.Role=="Пациент")
        {
            var results=await ListAsync(s,"referrals");var visits=await ListAsync(s,"visits");
            metrics=[Metric(appointments.Count(Upcoming),"Будущие записи","Ваши приёмы","#E4F2FF"),Metric(results.Count(r=>r.Status=="выполнено"),"Результаты","Готовые исследования","#E1F6ED"),Metric(visits.Count,"Посещения","В вашей истории","#FFF2DF")];
            rows=await ListAsync(s,"appointments");title="Мои записи";
        }
        else if(s.Role=="Врач")
        {
            var referrals=await ListAsync(s,"referrals");
            metrics=[Metric(appointments.Count(Today),"Приёмы сегодня","Ваше расписание","#E4F2FF"),Metric(appointments.Count(Upcoming),"Предстоящие","Запланированы","#E1F6ED"),Metric(referrals.Count(r=>r.Status=="выдано"),"Направления","Ожидают результата","#FFF2DF")];
            rows=await ListAsync(s,"appointments");title="Мои пациенты";
        }
        else
        {
            metrics=[Metric(appointments.Count(a=>Today(a)&&S(a,"status")=="запланирована"),"Записи сегодня","По всем врачам","#E4F2FF"),Metric(T("patient").Count(p=>S(p,"is_active")=="true"),"Пациенты","Активные карты","#E1F6ED"),Metric(T("invoice").Count(i=>S(i,"status")=="не оплачен"),"Неоплаченные счета","Нужны действия","#FFF2DF")];
            rows=await ListAsync(s,"appointments");title="Ближайшие записи";
        }
        return new(metrics,rows.Take(5).Select(r=>new DashboardItem(r.Cell1,r.Cell2,r.Cell3,r.Status)).ToArray(),title,"Демо поддерживает рабочие операции. Данные живут до закрытия приложения; PostgreSQL не используется.");
    }
    private static Record Row(Dictionary<string,string> v,string a,string b,string c,string d,string state,string detail)=>new(){Id=I(v,"id"),Cell1=a,Cell2=b,Cell3=c,Cell4=d,Status=state,Details=detail,Values=new(v)};
    public Task<List<Record>> ListAsync(ClinicSession s,string page,DateTime? from=null,DateTime? to=null)
    {
        Check(s);if(!Catalog.Sections(s.Role).Any(p=>p.Key==page))throw new InvalidOperationException("Раздел недоступен.");
        var rows=new List<Record>();
        string DateLabel(string v)=>DateTime.TryParse(v,out var date)?date.ToString("dd.MM.yyyy",Ru):v;
        string Lookup(string table,int id,string field="name")=>id==0?"—":S(Find(table,id),field);
        switch(page)
        {
            case "patients":
                foreach(var p in T("patient").Where(p=>s.Role=="Пациент"?I(p,"id")==s.PatientId:s.Role=="Врач"?T("appointment").Any(a=>I(a,"patient_id")==I(p,"id")&&Visible(s,a)):true))rows.Add(Row(p,Name(p),DateLabel(S(p,"birth_date")),S(p,"oms_policy"),S(p,"phone"),S(p,"is_active")=="true"?"активен":"архив","Карта: "+S(p,"card_number")+"\nАдрес: "+S(p,"address")+"\nДМС: "+S(p,"dms_policy")+"\nУчасток: "+S(p,"district")+"\nАллергии: "+S(p,"allergies")));break;
            case "schedule":
                foreach(var sc in T("schedule").Where(sc=>s.Role!="Врач"||I(sc,"employee_id")==s.EmployeeId))rows.Add(Row(sc,DateLabel(S(sc,"work_date")),Name(Find("employee",I(sc,"employee_id"))),S(sc,"start_time")+" — "+S(sc,"end_time"),Lookup("room",I(sc,"room_id"),"number"),"слот "+S(sc,"slot_minutes")+" мин","Рабочий график"));break;
            case "slots":
                foreach(var sc in T("schedule"))
                {
                    var date=DateTime.Parse(S(sc,"work_date"));var start=TimeSpan.Parse(S(sc,"start_time"));var end=TimeSpan.Parse(S(sc,"end_time"));var duration=I(sc,"slot_minutes");var e=Find("employee",I(sc,"employee_id"));
                    if(S(e,"is_active")!="true")continue;
                    for(var t=start;t+TimeSpan.FromMinutes(duration)<=end;t+=TimeSpan.FromMinutes(duration))
                    {
                        var text=t.ToString(@"hh\:mm");if(date+t<=DateTime.Now||date>DateTime.Today.AddDays(30)||T("appointment").Any(a=>I(a,"schedule_id")==I(sc,"id")&&S(a,"start_time")==text&&S(a,"status")!="отменена"))continue;
                        var v=new Dictionary<string,string>(sc){["slot"]=S(sc,"id")+"|"+text,["schedule_id"]=S(sc,"id"),["start_time"]=text};
                        rows.Add(Row(v,date.ToString("dd.MM.yyyy")+" "+text,Name(e),Lookup("specialty",I(e,"specialty_id")),Lookup("room",I(sc,"room_id"),"number"),"свободно",duration+" минут"));
                    }
                }break;
            case "appointments":
                foreach(var a in T("appointment").Where(a=>Visible(s,a)).Reverse())
                {
                    var sc=Find("schedule",I(a,"schedule_id"));var p=Find("patient",I(a,"patient_id"));var e=Find("employee",I(sc,"employee_id"));var values=new Dictionary<string,string>(a){["work_date"]=S(sc,"work_date")};
                    rows.Add(Row(values,DateLabel(S(sc,"work_date"))+" "+S(a,"start_time"),s.Role=="Пациент"?Name(e):Name(p),s.Role=="Врач"?S(p,"card_number"):s.Role=="Пациент"?Lookup("specialty",I(e,"specialty_id")):Name(e),Lookup("room",I(sc,"room_id"),"number"),S(a,"status"),"Пациент: "+Name(p)+"\nВрач: "+Name(e)+"\nКабинет: "+Lookup("room",I(sc,"room_id"),"number")));
                }break;
            case "visits":case "referrals":case "prescriptions":case "rendered":
                var table=page switch {"visits"=>"visit","referrals"=>"referral","prescriptions"=>"prescription",_=>"visit_service"};
                foreach(var item in T(table).AsEnumerable().Reverse())
                {
                    var visit=page=="visits"?item:Find("visit",I(item,"visit_id"));if(!VisitVisible(s,visit)&&!(page=="referrals"&&s.Role=="Врач"&&I(item,"consultation_doctor_id")==s.EmployeeId))continue;
                    var a=Appointment(visit);var p=Find("patient",I(a,"patient_id"));var sc=Find("schedule",I(a,"schedule_id"));var e=Find("employee",I(sc,"employee_id"));var v=new Dictionary<string,string>(item){["patient_id"]=S(a,"patient_id")};
                    if(page=="visits")rows.Add(Row(v,DateLabel(S(visit,"visited_at")),Name(p),Name(e),S(visit,"diagnosis_code"),S(visit,"is_closed")=="true"?"закрыт":"в работе","Жалобы: "+S(visit,"complaints")+"\nАнамнез: "+S(visit,"anamnesis")+"\nОсмотр: "+S(visit,"objective_status")+"\nРекомендации: "+S(visit,"recommendations")));
                    if(page=="referrals")rows.Add(Row(v,Lookup("service",I(item,"service_id")),Name(p),DateLabel(S(visit,"visited_at")),I(item,"executor_id")==0?"—":Name(Find("employee",I(item,"executor_id"))),S(item,"status"),S(item,"result")));
                    if(page=="prescriptions")rows.Add(Row(v,Lookup("drug",I(item,"drug_id")),Name(p),S(item,"dosage"),Name(e),S(item,"is_recipe")=="true"?"рецепт":"назначение",S(item,"instruction")));
                    if(page=="rendered")rows.Add(Row(v,Lookup("service",I(item,"service_id")),Name(p),DateLabel(S(item,"rendered_at")),(N(item,"price")*I(item,"quantity"))+" ₽",I(item,"invoice_id")==0?"без счёта":"счёт №"+S(item,"invoice_id"),"Количество: "+S(item,"quantity")));
                }break;
            case "invoices":
                foreach(var bill in T("invoice").Where(b=>s.Role!="Пациент"||I(b,"patient_id")==s.PatientId))
                {
                    var services=T("visit_service").Where(v=>I(v,"invoice_id")==I(bill,"id")).ToArray();var total=services.Sum(v=>N(v,"price")*I(v,"quantity"));var paid=T("payment").Where(p=>I(p,"invoice_id")==I(bill,"id")).Sum(p=>N(p,"amount"));
                    var v=new Dictionary<string,string>(bill){["total_text"]=total.ToString(CultureInfo.InvariantCulture),["paid"]=paid.ToString(CultureInfo.InvariantCulture)};
                    rows.Add(Row(v,"Счёт №"+S(bill,"id"),Name(Find("patient",I(bill,"patient_id"))),total+" ₽",paid+" ₽",S(bill,"status"),string.Join('\n',services.Select(x=>Lookup("service",I(x,"service_id"))+" × "+S(x,"quantity")))));
                }break;
            case "documents":
                foreach(var d in T("patient_document").Where(d=>s.Role=="Пациент"?I(d,"patient_id")==s.PatientId:s.Role=="Врач"?T("appointment").Any(a=>I(a,"patient_id")==I(d,"patient_id")&&Visible(s,a)):true))rows.Add(Row(d,S(d,"kind"),Name(Find("patient",I(d,"patient_id"))),DateLabel(S(d,"created_at")),Lookup("account",I(d,"account_id"),"login"),"оформлен",S(d,"content")));break;
            case "notifications":foreach(var n in T("notification").Where(n=>I(n,"patient_id")==s.PatientId).Reverse())rows.Add(Row(n,S(n,"title"),DateLabel(S(n,"created_at")),S(n,"message"),"",S(n,"is_read")=="true"?"прочитано":"новое",S(n,"message")));break;
            case "accounts":foreach(var a in T("account"))rows.Add(Row(a,S(a,"login"),Name(Find(I(a,"patient_id")==0?"employee":"patient",I(a,I(a,"patient_id")==0?"employee_id":"patient_id"))),Lookup("role",I(a,"role_id")),DateLabel(S(a,"last_login")),S(a,"is_active")=="true"?"активен":"заблокирован","Права определяются ролью"));break;
            case "employees":foreach(var e in T("employee"))rows.Add(Row(e,Name(e),Lookup("post",I(e,"post_id")),Lookup("specialty",I(e,"specialty_id")),S(e,"phone"),S(e,"is_active")=="true"?"активен":"архив","Демо-сотрудник"));break;
            case "services":foreach(var sv in T("service"))rows.Add(Row(sv,S(sv,"name"),Lookup("service_group",I(sv,"group_id")),S(sv,"price")+" ₽","",S(sv,"is_active")=="true"?"активна":"архив","Прайс-лист"));break;
            case "dictionary":foreach(var dt in new[]{"specialty","post","room","service_group","drug","diagnosis"})foreach(var item in T(dt)){var v=new Dictionary<string,string>(item){["table"]=dt,["name"]=dt=="room"?S(item,"number"):S(item,"name")};rows.Add(Row(v,dt,S(item,"code").Length>0?S(item,"code"):S(item,"id"),v["name"],"","справочник",v["name"]));}break;
            case "reports":
                var periodStart=from??new DateTime(DateTime.Today.Year,DateTime.Today.Month,1);var periodEnd=to??DateTime.Today;
                if(periodEnd<periodStart)throw new InvalidOperationException("Начало периода должно быть раньше окончания.");
                bool InPeriod(Dictionary<string,string> item,string key)=>DateTime.TryParse(S(item,key),out var date)&&date.Date>=periodStart.Date&&date.Date<=periodEnd.Date;
                var period=periodStart.ToString("dd.MM.yyyy")+" — "+periodEnd.ToString("dd.MM.yyyy");
                rows.Add(Row(new(){["id"]="1"},"Посещения за период",T("visit").Count(v=>InPeriod(v,"visited_at")).ToString(),period,"","отчёт",""));
                rows.Add(Row(new(){["id"]="2"},"Выручка за период",T("payment").Where(p=>InPeriod(p,"created_at")).Sum(p=>N(p,"amount"))+" ₽",period,"","отчёт",""));
                rows.Add(Row(new(){["id"]="3"},"Неоплаченные счета",T("invoice").Count(i=>S(i,"status")=="не оплачен").ToString(),"Все даты","","отчёт",""));
                foreach(var employee in T("employee"))
                {
                    var schedules=T("schedule").Where(sc=>I(sc,"employee_id")==I(employee,"id")&&InPeriod(sc,"work_date")).ToArray();if(schedules.Length==0)continue;
                    var capacity=schedules.Sum(sc=>(TimeSpan.Parse(S(sc,"end_time"))-TimeSpan.Parse(S(sc,"start_time"))).TotalMinutes/I(sc,"slot_minutes"));
                    var booked=T("appointment").Count(a=>S(a,"status")!="отменена"&&schedules.Any(sc=>I(sc,"id")==I(a,"schedule_id")));
                    rows.Add(Row(new(){["id"]=(100+I(employee,"id")).ToString()},"Загрузка: "+Name(employee),Math.Round(100*booked/Math.Max(1,capacity),1)+" %",period,"","отчёт",""));
                }
                var diagnosisId=200;
                foreach(var group in T("visit").Where(v=>InPeriod(v,"visited_at")&&S(v,"diagnosis_code")!="").GroupBy(v=>S(v,"diagnosis_code")))
                    rows.Add(Row(new(){["id"]=(++diagnosisId).ToString()},"Диагноз: "+group.Key,group.Count().ToString(),period,"","отчёт",T("diagnosis").FirstOrDefault(d=>S(d,"code")==group.Key)?.GetValueOrDefault("name","")??""));
                break;
            case "audit":foreach(var a in T("audit").AsEnumerable().Reverse())rows.Add(Row(a,DateLabel(S(a,"acted_at")),Lookup("account",I(a,"account_id"),"login"),S(a,"action"),S(a,"table_name"),"аудит",""));break;
        }
        return Task.FromResult(rows);
    }
    public async Task<Dictionary<string,List<Choice>>> ChoicesAsync(ClinicSession s)
    {
        Check(s);var result=new Dictionary<string,List<Choice>>();
        foreach(var (key,table) in new[]{("patients","patient"),("employees","employee"),("rooms","room"),("diagnoses","diagnosis"),("drugs","drug"),("services","service"),("roles","role"),("posts","post"),("specialties","specialty"),("groups","service_group"),("methods","pay_method")})
        {
            result[key]=T(table).Where(r=>S(r,"is_active")!="false"&&(table!="patient"||(s.Role=="Пациент"?I(r,"id")==s.PatientId:s.Role=="Врач"?T("appointment").Any(a=>I(a,"patient_id")==I(r,"id")&&Visible(s,a)):true))).Select(r=>new Choice(table=="diagnosis"?S(r,"code"):S(r,"id"),table is "patient" or "employee"?Name(r):table=="room"?S(r,"number"):S(r,"name"))).ToList();
        }
        result["doctors"]=T("employee").Where(e=>I(e,"specialty_id")>0&&S(e,"is_active")=="true").Select(e=>new Choice(S(e,"id"),Name(e))).ToList();
        result["visits"]=T("visit").Where(v=>VisitVisible(s,v)).Select(v=>new Choice(S(v,"id"),Name(Find("patient",I(Appointment(v),"patient_id")))+" · "+S(v,"visited_at"))).ToList();
        result["open_visits"]=result["visits"].Where(v=>S(Find("visit",int.Parse(v.Id)),"is_closed")!="true").ToList();
        result["slots"]=s.Role is "Пациент" or "Регистратор"?(await ListAsync(s,"slots")).Select(r=>new Choice(r.Values["slot"],r.Cell1+" · "+r.Cell2)).ToList():[];
        result["gender"]=[new("М","Мужской"),new("Ж","Женский")];result["boolean"]=[new("false","Нет"),new("true","Да")];
        result["document_types"]=s.Role=="Врач"?[new("Справка","Справка")]:[new("Согласие на обработку данных","Согласие на обработку данных"),new("Согласие на вмешательство","Согласие на вмешательство"),new("Договор платных услуг","Договор платных услуг")];
        result["dictionary_types"]=new[]{"specialty","post","room","service_group","drug","diagnosis"}.Select(t=>new Choice(t,t)).ToList();return result;
    }
    public Task<int> SaveAsync(ClinicSession s,Mutation mutation)
    {
        Check(s);var kind=mutation.Kind;var id=mutation.Id;var v=new Dictionary<string,string>(mutation.Values);string table=kind;
        foreach(var f in Catalog.Fields(kind))if(f.Required&&S(v,f.Key).Length==0)throw new InvalidOperationException("Заполните «"+f.Label+"».");
        switch(kind)
        {
            case "password":
                if(_passwords.GetValueOrDefault(s.AccountId)!=v.GetValueOrDefault("old_password"))throw new InvalidOperationException("Текущий пароль неверен.");
                if(v.GetValueOrDefault("new_password","").Length<8||v.GetValueOrDefault("new_password")!=v.GetValueOrDefault("confirmation"))throw new InvalidOperationException("Пароль от 8 символов; подтверждение должно совпадать.");
                _passwords[s.AccountId]=v["new_password"];return Task.FromResult(s.AccountId);
            case "patient":Check(s,"Регистратор");if(S(v,"oms_policy").Length!=16||!S(v,"oms_policy").All(char.IsDigit))throw new InvalidOperationException("Полис ОМС: 16 цифр.");if(!DateTime.TryParse(S(v,"birth_date"),out var birth)||birth>DateTime.Today)throw new InvalidOperationException("Проверьте дату рождения.");if(T("patient").Any(p=>S(p,"oms_policy")==S(v,"oms_policy")&&I(p,"id")!=id))throw new InvalidOperationException("Полис уже зарегистрирован.");if(id==0){v["card_number"]="ДЕМО-"+Guid.NewGuid().ToString("N")[..8];v["is_active"]="true";}break;
            case "schedule":
                Check(s,"Регистратор");var from=TimeSpan.Parse(S(v,"start_time"));var to=TimeSpan.Parse(S(v,"end_time"));if(from>=to||I(v,"slot_minutes")<5||I(v,"slot_minutes")>180||(to-from).TotalMinutes<I(v,"slot_minutes"))throw new InvalidOperationException("Проверьте время и длительность слота.");if(id>0&&T("appointment").Any(a=>I(a,"schedule_id")==id&&S(a,"status")!="отменена"))throw new InvalidOperationException("Сначала перенесите записи из графика.");if(T("schedule").Any(sc=>I(sc,"id")!=id&&S(sc,"work_date")==S(v,"work_date")&&(I(sc,"employee_id")==I(v,"employee_id")||(I(sc,"room_id")==I(v,"room_id")&&TimeSpan.Parse(S(sc,"start_time"))<to&&TimeSpan.Parse(S(sc,"end_time"))>from))))throw new InvalidOperationException("График врача или кабинет уже занят.");break;
            case "booking":
                Check(s,"Пациент","Регистратор");table="appointment";if(s.Role=="Пациент")v["patient_id"]=s.PatientId.ToString()!;
                var parts=S(v,"slot").Split('|');if(parts.Length!=2)throw new InvalidOperationException("Выберите слот.");var sc=Find("schedule",int.Parse(parts[0]));var time=TimeSpan.Parse(parts[1]);var date=DateTime.Parse(S(sc,"work_date"));var duration=I(sc,"slot_minutes");
                if(S(Find("patient",I(v,"patient_id")),"is_active")!="true"||S(Find("employee",I(sc,"employee_id")),"is_active")!="true")throw new InvalidOperationException("Пациент или врач в архиве.");
                if(date+time<=DateTime.Now||time<TimeSpan.Parse(S(sc,"start_time"))||time+TimeSpan.FromMinutes(duration)>TimeSpan.Parse(S(sc,"end_time"))||(time-TimeSpan.Parse(S(sc,"start_time"))).TotalMinutes%duration!=0)throw new InvalidOperationException("Слот недоступен.");
                if(id>0){var old=Find("appointment",id);if(I(old,"patient_id")!=I(v,"patient_id")||S(old,"status")!="запланирована"||T("visit").Any(vi=>I(vi,"appointment_id")==id))throw new InvalidOperationException("Нельзя перенести эту запись.");}
                if(T("appointment").Any(a=>I(a,"id")!=id&&S(a,"status")!="отменена"&&((S(a,"schedule_id")==parts[0]&&S(a,"start_time")==parts[1])||(I(a,"patient_id")==I(v,"patient_id")&&DateTime.Parse(S(Find("schedule",I(a,"schedule_id")),"work_date"))+TimeSpan.Parse(S(a,"start_time"))<date+time+TimeSpan.FromMinutes(duration)&&DateTime.Parse(S(Find("schedule",I(a,"schedule_id")),"work_date"))+TimeSpan.Parse(S(a,"start_time"))+TimeSpan.FromMinutes(I(Find("schedule",I(a,"schedule_id")),"slot_minutes"))>date+time))))throw new InvalidOperationException("Слот занят или у пациента уже есть запись.");
                v["schedule_id"]=parts[0];v["start_time"]=parts[1];v["status"]="запланирована";v["source"]=s.Role=="Пациент"?"приложение":"регистратура";Notify(I(v,"patient_id"),"Запись на приём",date.ToString("dd.MM.yyyy")+" "+parts[1]);break;
            case "visit":OwnVisit(s,id);break;
            case "prescription":case "referral":OwnVisit(s,I(v,"visit_id"));if(id>0){var old=Find(kind,id);if(I(old,"visit_id")!=I(v,"visit_id"))throw new InvalidOperationException("Нельзя переносить запись в другой приём.");}if(kind=="referral"&&id==0)v["status"]="выдано";break;
            case "result":table="referral";var referral=Find(table,id);Check(s,"Врач");if(S(referral,"status")=="отменено"||(!VisitVisible(s,Find("visit",I(referral,"visit_id")))&&I(referral,"consultation_doctor_id")!=s.EmployeeId))throw new InvalidOperationException("Направление недоступно.");if(v.TryGetValue("file_data",out var file)&&Convert.FromBase64String(file).Length>10*1024*1024)throw new InvalidOperationException("Максимальный размер файла — 10 МБ.");v["status"]="выполнено";v["executor_id"]=s.EmployeeId.ToString()!;v["done_at"]=DateTime.Now.ToString("s");Notify(I(Appointment(Find("visit",I(referral,"visit_id"))),"patient_id"),"Результат готов","Исследование №"+id);break;
            case "rendered":Check(s,"Регистратор");table="visit_service";if(id>0&&I(Find(table,id),"invoice_id")>0)throw new InvalidOperationException("Услуга уже в счёте.");if(I(v,"quantity")<1||I(v,"quantity")>1000)throw new InvalidOperationException("Количество должно быть положительным.");v["price"]=S(Find("service",I(v,"service_id")),"price");v["rendered_at"]=DateTime.Now.ToString("s");break;
            case "payment":
                Check(s,"Регистратор");table="payment";var invoice=Find("invoice",id);var total=T("visit_service").Where(vs=>I(vs,"invoice_id")==id).Sum(vs=>N(vs,"price")*I(vs,"quantity"));var paid=T("payment").Where(p=>I(p,"invoice_id")==id).Sum(p=>N(p,"amount"));var amount=N(v,"amount");if(S(invoice,"status")!="не оплачен"||amount<=0||amount>total-paid)throw new InvalidOperationException("Проверьте сумму платежа.");if(S(Find("pay_method",I(v,"method_id")),"name")=="ДМС"&&S(Find("patient",I(invoice,"patient_id")),"dms_policy")=="")throw new InvalidOperationException("У пациента нет ДМС.");if(paid+amount==total)invoice["status"]="оплачен";v["invoice_id"]=id.ToString();v["created_at"]=DateTime.Now.ToString("s");v["account_id"]=s.AccountId.ToString();id=0;break;
            case "document":Check(s,"Врач","Регистратор");if(id!=0)throw new InvalidOperationException("Оформленный документ нельзя перезаписывать.");table="patient_document";v["account_id"]=s.AccountId.ToString();v["created_at"]=DateTime.Now.ToString("s");if(s.Role=="Врач"&&(!T("appointment").Any(a=>I(a,"patient_id")==I(v,"patient_id")&&Visible(s,a))||S(v,"kind")!="Справка"))throw new InvalidOperationException("Документ недоступен.");break;
            case "account":
                Check(s,"Администратор");if(S(v,"login").Length<3||S(v,"login").Length>50)throw new InvalidOperationException("Логин: 3–50 символов.");var role=S(Find("role",I(v,"role_id")),"name");if(role=="Пациент"?(I(v,"patient_id")==0||I(v,"employee_id")>0):(I(v,"employee_id")==0||I(v,"patient_id")>0))throw new InvalidOperationException("Укажите владельца аккаунта по его роли.");if(id==s.AccountId&&role!="Администратор")throw new InvalidOperationException("Нельзя снять свою роль.");if(T("account").Any(a=>I(a,"id")!=id&&S(a,"login").Equals(S(v,"login"),StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Логин уже занят.");if((id==0||S(v,"password").Length>0)&&S(v,"password").Length<8)throw new InvalidOperationException("Пароль от 8 символов.");if(id==0)v["is_active"]="true";break;
            case "employee":case "service":Check(s,"Администратор");if(kind=="service"&&N(v,"price")<0)throw new InvalidOperationException("Цена не может быть отрицательной.");if(id==0)v["is_active"]="true";break;
            case "dictionary":Check(s,"Администратор");table=S(v,"table");if(!new[]{"specialty","post","room","service_group","drug","diagnosis"}.Contains(table))throw new InvalidOperationException("Справочник недоступен.");if(table=="room"){v["number"]=S(v,"name");v["floor"]=S(v,"code");}if(table=="diagnosis"&&id==0&&T(table).FirstOrDefault(r=>S(r,"code")==S(v,"code")) is { } existing)id=I(existing,"id");break;
            default:throw new InvalidOperationException("Действие недоступно.");
        }
        var password=S(v,"password");v.Remove("password");var result=Put(table,v,id);if(kind=="account"&&password.Length>0)_passwords[result]=password;Audit(s,table);return Task.FromResult(result);
    }
    public Task ActAsync(ClinicSession s,string action,int id,Dictionary<string,string>? values=null)
    {
        Check(s);string table;
        switch(action)
        {
            case "start_visit":
                Check(s,"Врач");table="visit";var a=Find("appointment",id);if(!Visible(s,a)||S(a,"status")!="запланирована"||DateTime.Parse(S(Find("schedule",I(a,"schedule_id")),"work_date"))>DateTime.Today)throw new InvalidOperationException("Приём недоступен.");if(!T(table).Any(v=>I(v,"appointment_id")==id))Seed(table,("appointment_id",id.ToString()),("visited_at",DateTime.Now.ToString("s")),("is_closed","false"));break;
            case "close_visit":table="visit";OwnVisit(s,id);var vi=Find(table,id);if(S(vi,"diagnosis_code")=="")throw new InvalidOperationException("Укажите диагноз.");vi["is_closed"]="true";Appointment(vi)["status"]="завершена";break;
            case "cancel":case "no_show":
                Check(s,action=="cancel"?["Пациент","Регистратор"]:["Врач","Регистратор"]);table="appointment";var ap=Find(table,id);if(!Visible(s,ap)||S(ap,"status")!="запланирована"||T("visit").Any(v=>I(v,"appointment_id")==id))throw new InvalidOperationException("Эту запись нельзя изменить.");ap["status"]=action=="cancel"?"отменена":"неявка";Notify(I(ap,"patient_id"),"Приём изменён",ap["status"]);break;
            case "cancel_referral":table="referral";var r=Find(table,id);OwnVisit(s,I(r,"visit_id"),true);if(S(r,"status")!="выдано")throw new InvalidOperationException("Направление уже выполнено или отменено.");r["status"]="отменено";break;
            case "mark_read":Check(s,"Пациент");table="notification";var n=Find(table,id);if(I(n,"patient_id")!=s.PatientId)throw new InvalidOperationException("Уведомление недоступно.");n["is_read"]="true";break;
            case "create_invoice":
                Check(s,"Регистратор");table="invoice";var svc=Find("visit_service",id);if(I(svc,"invoice_id")>0)throw new InvalidOperationException("Счёт уже создан.");var p=I(Appointment(Find("visit",I(svc,"visit_id"))),"patient_id");var bill=Put(table,new(){["patient_id"]=p.ToString(),["status"]="не оплачен",["created_at"]=DateTime.Now.ToString("s")});foreach(var vs in T("visit_service").Where(vs=>I(vs,"invoice_id")==0&&I(Appointment(Find("visit",I(vs,"visit_id"))),"patient_id")==p))vs["invoice_id"]=bill.ToString();Notify(p,"Новый счёт","Счёт №"+bill);break;
            default:
                if(!new[]{"toggle_patient","toggle_account","toggle_employee","toggle_service"}.Contains(action))throw new InvalidOperationException("Действие недоступно.");table=action[7..];Check(s,table=="patient"?"Регистратор":"Администратор");if(table=="account"&&id==s.AccountId)throw new InvalidOperationException("Нельзя блокировать себя.");var row=Find(table,id);row["is_active"]=S(row,"is_active")=="true"?"false":"true";break;
        }
        Audit(s,table);return Task.CompletedTask;
    }
    public Task RegisterAsync(Dictionary<string,string> v)
    {
        if(S(v,"password").Length<8||S(v,"login").Length<3)throw new InvalidOperationException("Логин от 3 символов, пароль от 8.");var p=T("patient").FirstOrDefault(p=>S(p,"oms_policy")==S(v,"oms_policy")&&S(p,"birth_date")==S(v,"birth_date")&&S(p,"is_active")=="true")??throw new InvalidOperationException("Пациент не найден.");if(T("account").Any(a=>I(a,"patient_id")==I(p,"id")||S(a,"login").Equals(S(v,"login"),StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Учётная запись уже существует.");var id=Put("account",new(){["login"]=S(v,"login"),["role_id"]="1",["patient_id"]=S(p,"id"),["is_active"]="true"});_passwords[id]=S(v,"password");return Task.CompletedTask;
    }
    public Task<byte[]?> AttachmentAsync(ClinicSession s,int id)
    {
        Check(s,"Пациент","Врач");var r=Find("referral",id);if(!VisitVisible(s,Find("visit",I(r,"visit_id")))&&!(s.Role=="Врач"&&I(r,"consultation_doctor_id")==s.EmployeeId))throw new InvalidOperationException("Файл недоступен.");return Task.FromResult<byte[]?>(S(r,"file_data")==""?null:Convert.FromBase64String(S(r,"file_data")));
    }
    public Task<string> BackupAsync(ClinicSession s){Check(s,"Администратор");return Task.FromResult(JsonSerializer.Serialize(new DemoBackup(1,_tables,_passwords)));}
    public Task RestoreAsync(ClinicSession s,string json)
    {
        Check(s,"Администратор");var backup=JsonSerializer.Deserialize<DemoBackup>(json)??throw new InvalidOperationException("Неверный формат копии.");if(backup.Version!=1||!backup.Tables.ContainsKey("account"))throw new InvalidOperationException("Неверный формат копии.");
        foreach(var (table,records) in backup.Tables)foreach(var record in records){var id=I(record,"id");if(T(table).Any(r=>I(r,"id")==id)){if(table is not ("audit" or "payment" or "patient_document"))Put(table,record,id);}else T(table).Add(new(record));}
        foreach(var (id,password) in backup.Passwords)_passwords[id]=password;return Task.CompletedTask;
    }
    public sealed record DemoBackup(int Version,Dictionary<string,List<Dictionary<string,string>>> Tables,Dictionary<int,string> Passwords);
}
