using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Clinic.Workspace;
using Npgsql;

namespace Clinic.Data;

public sealed partial class ClinicDatabase
{
    private static string Get(Dictionary<string,string> v,string key)=>v.GetValueOrDefault(key,"").Trim();
    private static int Int(Dictionary<string,string> v,string key)=>int.TryParse(Get(v,key),out var n)?n:throw new InvalidOperationException("Укажите значение: "+key);
    private static decimal Money(Dictionary<string,string> v,string key)=>decimal.TryParse(Get(v,key).Replace(',','.'),NumberStyles.Number,CultureInfo.InvariantCulture,out var n)?n:throw new InvalidOperationException("Некорректная сумма.");
    private static DateTime Date(Dictionary<string,string> v,string key)=>DateTime.TryParseExact(Get(v,key),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d:throw new InvalidOperationException("Дата должна быть в формате ГГГГ-ММ-ДД.");
    private static TimeSpan Time(Dictionary<string,string> v,string key)=>TimeSpan.TryParseExact(Get(v,key),@"hh\:mm",CultureInfo.InvariantCulture,out var t)?t:throw new InvalidOperationException("Время должно быть в формате ЧЧ:ММ.");
    private static object? Value(Dictionary<string,string> v,string key)
    {
        var text=Get(v,key);if(text.Length==0)return null;
        if(key.EndsWith("_id")||key is "slot_minutes" or "quantity")return Int(v,key);
        if(key is "birth_date" or "work_date" or "valid_until")return Date(v,key);
        if(key is "start_time" or "end_time")return Time(v,key);
        if(key is "price" or "amount")return Money(v,key);
        if(key=="is_recipe")return text=="true";
        return text;
    }
    private static void ValidateFields(Mutation m)
    {
        foreach(var f in Catalog.Fields(m.Kind))
        {
            var value=Get(m.Values,f.Key);
            if(f.Required&&string.IsNullOrEmpty(value))throw new InvalidOperationException("Заполните поле «"+f.Label+"».");
            if(!f.Multiline&&value.Length>300)throw new InvalidOperationException("Слишком длинное поле «"+f.Label+"».");
        }
    }
    private static async Task<int> Write(NpgsqlConnection c,string table,int id,Dictionary<string,object?> data)
    {
        var fields=data.Keys.ToArray();
        var sql=id==0?$"INSERT INTO {table} ({string.Join(',',fields)}) VALUES ({string.Join(',',fields.Select(k=>'@'+k))}) RETURNING id":
            $"UPDATE {table} SET {string.Join(',',fields.Select(k=>k+"=@"+k))} WHERE id=@id RETURNING id";
        var args=data.Select(p=>(p.Key,p.Value)).ToList();if(id!=0)args.Add(("id",id));
        var value=await Scalar(c,sql,args.ToArray());return value is int n?n:throw new InvalidOperationException("Запись больше не существует.");
    }
    private static Dictionary<string,object?> FormData(Mutation m,string kind)=>Catalog.Fields(kind)
        .Where(f=>f.Key is not ("file_name" or "password" or "permissions" or "slot"))
        .ToDictionary(f=>f.Key,f=>Value(m.Values,f.Key));
    private static async Task<int> OwnVisit(NpgsqlConnection c,ClinicSession s,int id,bool allowClosed=false)
    {
        var result=await Scalar(c,"SELECT a.patient_id FROM visit v JOIN appointment a ON a.id=v.appointment_id JOIN schedule sc ON sc.id=a.schedule_id WHERE v.id=@id AND sc.employee_id=@e AND (@closed OR NOT v.is_closed) FOR UPDATE OF v",("id",id),("e",s.EmployeeId??0),("closed",allowClosed));
        return result is int p?p:throw new InvalidOperationException("Приём закрыт или принадлежит другому врачу.");
    }
    private static async Task CheckChild(NpgsqlConnection c,ClinicSession s,string table,int id,int visit)
    {
        if(id==0)return;
        var actual=await Scalar(c,$"SELECT visit_id FROM {table} WHERE id=@id",("id",id));
        if(actual is not int v || v!=visit)throw new InvalidOperationException("Нельзя переносить запись в другой приём.");
    }
    public async Task<int> SaveAsync(ClinicSession session,Mutation mutation)
    {
        ValidateFields(mutation);
        await using var c=await OpenAsync();await using var tx=await c.BeginTransactionAsync();
        var m=mutation;var v=m.Values;int id=m.Id;int? patient=null;int? auditId=null;string table=m.Kind;
        switch(m.Kind)
        {
            case "patient":
                await CheckAsync(c,session,"Регистратор");
                if(Date(v,"birth_date")>DateTime.Today)throw new InvalidOperationException("Дата рождения не может быть в будущем.");
                if(Get(v,"oms_policy").Length!=16||!Get(v,"oms_policy").All(char.IsDigit))throw new InvalidOperationException("Полис ОМС должен содержать 16 цифр.");
                var pData=FormData(m,"patient");
                if(id==0)pData["card_number"]="П-"+Guid.NewGuid().ToString("N")[..12];
                id=await Write(c,"patient",id,pData);patient=id;break;
            case "schedule":
                await CheckAsync(c,session,"Регистратор");
                var doctor=Int(v,"employee_id");var room=Int(v,"room_id");var date=Date(v,"work_date");var start=Time(v,"start_time");var end=Time(v,"end_time");var minutes=Int(v,"slot_minutes");
                if(end<=start||minutes<5||minutes>180||(end-start).TotalMinutes<minutes)throw new InvalidOperationException("Проверьте время и длительность приёма (5–180 минут).");
                await Exec(c,"SELECT pg_advisory_xact_lock(901,@id)",("id",room));
                if(id>0)await Exec(c,"SELECT pg_advisory_xact_lock(902,@id)",("id",id));
                if((long)(await Scalar(c,"SELECT count(*) FROM schedule WHERE room_id=@room AND work_date=@date AND id<>@id AND start_time<@end AND end_time>@start",("room",room),("date",date),("id",id),("end",end),("start",start))??0L)>0)throw new InvalidOperationException("Кабинет уже занят в это время.");
                if(id>0&&(long)(await Scalar(c,"SELECT count(*) FROM appointment WHERE schedule_id=@id AND status<>'отменена'",("id",id))??0L)>0)throw new InvalidOperationException("Сначала перенесите записи из этого графика.");
                if(await Scalar(c,"SELECT e.id FROM employee e JOIN account ac ON ac.employee_id=e.id JOIN role r ON r.id=ac.role_id WHERE e.id=@id AND e.is_active AND r.name='Врач'",("id",doctor)) is null)throw new InvalidOperationException("Выберите активного врача.");
                id=await Write(c,"schedule",id,FormData(m,"schedule"));break;
            case "booking":
                await CheckAsync(c,session,"Пациент","Регистратор");table="appointment";
                patient=session.Role=="Пациент"?session.PatientId:Int(v,"patient_id");
                if(patient is null)throw new InvalidOperationException("Не выбран пациент.");
                var parts=Get(v,"slot").Split('|');
                if(parts.Length!=2||!int.TryParse(parts[0],out var schedule)||!TimeSpan.TryParseExact(parts[1],@"hh\:mm",CultureInfo.InvariantCulture,out var slot))throw new InvalidOperationException("Выберите свободное время.");
                await Exec(c,"SELECT pg_advisory_xact_lock(902,@id)",("id",schedule));
                await Exec(c,"SELECT pg_advisory_xact_lock(903,@id)",("id",patient.Value));
                if(id>0)
                {
                    await Exec(c,"SELECT id FROM appointment WHERE id=@id FOR UPDATE",("id",id));
                    var owner=await Scalar(c,"SELECT patient_id FROM appointment WHERE id=@id AND status='запланирована' AND NOT EXISTS(SELECT 1 FROM visit WHERE appointment_id=@id)",("id",id));
                    if(owner is not int p||p!=patient)throw new InvalidOperationException("Перенести можно только незавершённую запись этого пациента.");
                }
                if(await Scalar(c,"SELECT id FROM patient WHERE id=@id AND is_active",("id",patient)) is null)throw new InvalidOperationException("Пациент находится в архиве.");
                DateTime slotDate;int duration;
                await using(var cmd=Command(c,"SELECT sc.work_date,sc.start_time,sc.end_time,sc.slot_minutes FROM schedule sc JOIN employee e ON e.id=sc.employee_id WHERE sc.id=@id AND e.is_active",("id",schedule)))
                await using(var reader=await cmd.ExecuteReaderAsync())
                {
                    if(!await reader.ReadAsync())throw new InvalidOperationException("График недоступен.");
                    slotDate=reader.GetDateTime(0).Date+slot;duration=reader.GetInt32(3);var from=reader.GetTimeSpan(1);var to=reader.GetTimeSpan(2);
                    if(slotDate<=DateTime.Now||slot<from||slot+TimeSpan.FromMinutes(duration)>to||(slot-from).TotalMinutes%duration!=0)throw new InvalidOperationException("Этот слот недоступен для записи.");
                }
                if((long)(await Scalar(c,"SELECT count(*) FROM appointment a JOIN schedule sc ON sc.id=a.schedule_id WHERE a.patient_id=@p AND a.id<>@id AND a.status<>'отменена' AND sc.work_date+a.start_time<@end AND sc.work_date+a.start_time+sc.slot_minutes*interval '1 minute'>@start",("p",patient),("id",id),("start",slotDate),("end",slotDate.AddMinutes(duration)))??0L)>0)throw new InvalidOperationException("У пациента уже есть запись на это время.");
                id=await Write(c,"appointment",id,new(){["patient_id"]=patient,["schedule_id"]=schedule,["start_time"]=slot,["status"]="запланирована",["source"]=session.Role=="Пациент"?"приложение":"регистратура"});
                await Notify(c,patient.Value,m.Id==0?"Запись на приём":"Приём перенесён",slotDate.ToString("dd.MM.yyyy HH:mm")+". Запись №"+id);break;
            case "visit":
                await CheckAsync(c,session,"Врач");patient=await OwnVisit(c,session,id);id=await Write(c,"visit",id,FormData(m,"visit"));break;
            case "prescription":
            case "referral":
                await CheckAsync(c,session,"Врач");var visit=Int(v,"visit_id");patient=await OwnVisit(c,session,visit);await CheckChild(c,session,m.Kind,id,visit);
                id=await Write(c,m.Kind,id,FormData(m,m.Kind));break;
            case "result":
                await CheckAsync(c,session,"Врач");table="referral";
                patient=await ReferralPatient(c,session,id);
                if(string.IsNullOrWhiteSpace(Get(v,"result")))throw new InvalidOperationException("Введите заключение.");
                var resultData=new Dictionary<string,object?> { ["result"]=Get(v,"result"),["status"]="выполнено",["executor_id"]=session.EmployeeId,["done_at"]=DateTime.Now };
                if(v.TryGetValue("file_data",out var content)&&content.Length>0)
                {
                    var bytes=Convert.FromBase64String(content);if(bytes.Length>10*1024*1024)throw new InvalidOperationException("Максимальный размер файла — 10 МБ.");
                    resultData["file_name"]=Get(v,"file_name");resultData["file_data"]=bytes;
                }
                id=await Write(c,"referral",id,resultData);await Notify(c,patient.Value,"Результат готов","Исследование №"+id+". Результат доступен в разделе «Исследования».");break;
            case "rendered":
                await CheckAsync(c,session,"Регистратор");table="visit_service";
                if(id>0)await Exec(c,"SELECT id FROM visit_service WHERE id=@id FOR UPDATE",("id",id));
                patient=Convert.ToInt32(await Scalar(c,"SELECT a.patient_id FROM visit v JOIN appointment a ON a.id=v.appointment_id WHERE v.id=@id",("id",Int(v,"visit_id")))??throw new InvalidOperationException("Приём не найден."));
                if(id>0&&await Scalar(c,"SELECT id FROM visit_service WHERE id=@id AND invoice_id IS NOT NULL",("id",id)) is not null)throw new InvalidOperationException("Услуга уже включена в счёт.");
                var rendered=FormData(m,"rendered");if(Int(v,"quantity")<1||Int(v,"quantity")>1000)throw new InvalidOperationException("Количество должно быть от 1 до 1000.");
                rendered["price"]=await Scalar(c,"SELECT price FROM service WHERE id=@id AND is_active",("id",Int(v,"service_id")))??throw new InvalidOperationException("Услуга недоступна.");rendered["rendered_at"]=DateTime.Now;
                id=await Write(c,"visit_service",id,rendered);break;
            case "payment":
                await CheckAsync(c,session,"Регистратор");table="payment";
                await Exec(c,"SELECT id FROM invoice WHERE id=@id FOR UPDATE",("id",id));
                decimal total,paid;string state;
                await using(var cmd=Command(c,"SELECT i.total,COALESCE((SELECT sum(amount) FROM payment WHERE invoice_id=i.id),0),i.status,i.patient_id FROM v_invoice i WHERE id=@id",("id",id)))
                await using(var reader=await cmd.ExecuteReaderAsync())
                {
                    if(!await reader.ReadAsync())throw new InvalidOperationException("Счёт не найден.");total=reader.GetDecimal(0);paid=reader.GetDecimal(1);state=reader.GetString(2);patient=reader.GetInt32(3);
                }
                var amount=Money(v,"amount");var method=Int(v,"method_id");
                if(state!="не оплачен"||amount<=0||amount>total-paid)throw new InvalidOperationException("Сумма должна быть больше нуля и не превышать остаток по счёту.");
                if(await Scalar(c,"SELECT name FROM pay_method WHERE id=@id",("id",method)) is string methodName&&methodName=="ДМС"&&await Scalar(c,"SELECT dms_policy FROM patient WHERE id=@id",("id",patient)) is not string)throw new InvalidOperationException("У пациента нет полиса ДМС.");
                auditId=Convert.ToInt32(await Scalar(c,"INSERT INTO payment(invoice_id,method_id,amount,account_id) VALUES(@i,@m,@a,@u) RETURNING id",("i",id),("m",method),("a",amount),("u",session.AccountId)));
                if(paid+amount==total)await Exec(c,"UPDATE invoice SET status='оплачен',method_id=@m,paid_at=now() WHERE id=@id",("m",method),("id",id));
                await Notify(c,patient.Value,"Оплата получена","Счёт №"+id+" · "+amount.ToString("0.00")+" ₽");break;
            case "document":
                await CheckAsync(c,session,"Регистратор","Врач");table="patient_document";patient=Int(v,"patient_id");
                if(session.Role=="Врач"&&await Scalar(c,"SELECT a.id FROM appointment a JOIN schedule sc ON sc.id=a.schedule_id WHERE a.patient_id=@p AND sc.employee_id=@e LIMIT 1",("p",patient),("e",session.EmployeeId)) is null)throw new InvalidOperationException("Этот пациент не относится к вашим приёмам.");
                if(session.Role=="Врач"&&Get(v,"kind")!="Справка")throw new InvalidOperationException("Врач оформляет справки.");
                if(id!=0)throw new InvalidOperationException("Оформленный документ нельзя перезаписывать.");
                var doc=FormData(m,"document");doc["account_id"]=session.AccountId;id=await Write(c,"patient_document",0,doc);break;
            case "account":
                await CheckAsync(c,session,"Администратор");table="account";
                var accountRole=await Scalar(c,"SELECT name FROM role WHERE id=@id",("id",Int(v,"role_id")));
                if(accountRole is not string r||r is not ("Врач" or "Регистратор" or "Администратор" or "Пациент"))throw new InvalidOperationException("Выберите одну из четырёх ролей проекта.");
                var employee=Get(v,"employee_id");var accountPatient=Get(v,"patient_id");
                if(r=="Пациент"?(accountPatient==""||employee!=""):(employee==""||accountPatient!=""))throw new InvalidOperationException("Пациенту выбирается пациент, сотруднику — сотрудник.");
                if(id==session.AccountId&&r!="Администратор")throw new InvalidOperationException("Нельзя снять свою роль администратора.");
                var login=Get(v,"login");if(login.Length<3||login.Length>50)throw new InvalidOperationException("Логин: от 3 до 50 символов.");
                var secret=v.GetValueOrDefault("password","");if((id==0||secret.Length>0)&&secret.Length<8)throw new InvalidOperationException("Пароль должен содержать минимум 8 символов.");
                var ac=new Dictionary<string,object?> { ["login"]=login,["role_id"]=Int(v,"role_id"),["employee_id"]=Value(v,"employee_id"),["patient_id"]=Value(v,"patient_id") };
                if(secret.Length>0)ac["password_hash"]=await Scalar(c,"SELECT crypt(@p,gen_salt('bf'))",("p",secret));
                id=await Write(c,"account",id,ac);break;
            case "employee":
            case "service":
                await CheckAsync(c,session,"Администратор");
                if(m.Kind=="service"&&Money(v,"price")<0)throw new InvalidOperationException("Цена не может быть отрицательной.");
                id=await Write(c,m.Kind,id,FormData(m,m.Kind));break;
            case "dictionary":
                await CheckAsync(c,session,"Администратор");table=Get(v,"table");
                if(!new[]{"specialty","post","room","service_group","drug","diagnosis"}.Contains(table))throw new InvalidOperationException("Неизвестный справочник.");
                if(table=="diagnosis")
                {
                    var code=Get(v,"code");if(code.Length==0||code.Length>10)throw new InvalidOperationException("Укажите код МКБ-10.");
                    await Exec(c,"INSERT INTO diagnosis(code,name) VALUES(@c,@n) ON CONFLICT(code) DO UPDATE SET name=EXCLUDED.name",("c",code),("n",Get(v,"name")));
                }
                else
                {
                    var dictionary=new Dictionary<string,object?> { [table=="room"?"number":"name"]=Get(v,"name") };
                    if(table=="room") dictionary["floor"]=short.TryParse(Get(v,"code"),out var floor)?floor:1;
                    id=await Write(c,table,id,dictionary);
                }
                break;
            default:throw new InvalidOperationException("Неизвестная операция.");
        }
        await Audit(c,session,m.Id==0||m.Kind=="payment"?"INSERT":"UPDATE",table,auditId??id,patient);await tx.CommitAsync();return id;
    }
    private static async Task<int> ReferralPatient(NpgsqlConnection c,ClinicSession s,int id)
    {
        var value=await Scalar(c,"SELECT a.patient_id FROM referral r JOIN visit v ON v.id=r.visit_id JOIN appointment a ON a.id=v.appointment_id JOIN schedule sc ON sc.id=a.schedule_id WHERE r.id=@id AND r.status<>'отменено' AND (sc.employee_id=@e OR r.consultation_doctor_id=@e) FOR UPDATE OF r",("id",id),("e",s.EmployeeId));
        return value is int p?p:throw new InvalidOperationException("Направление недоступно или отменено.");
    }
    public async Task ActAsync(ClinicSession session,string action,int id,Dictionary<string,string>? values=null)
    {
        await using var c=await OpenAsync();await using var tx=await c.BeginTransactionAsync();int? patient=null;string table;var auditAction="UPDATE";
        switch(action)
        {
            case "cancel":
            case "no_show":
                await CheckAsync(c,session,action=="no_show"?["Врач","Регистратор"]:["Пациент","Регистратор"]);table="appointment";
                await Exec(c,"SELECT id FROM appointment WHERE id=@id FOR UPDATE",("id",id));
                var found=await Scalar(c,"SELECT a.patient_id FROM appointment a JOIN schedule sc ON sc.id=a.schedule_id WHERE a.id=@id AND a.status='запланирована' AND NOT EXISTS(SELECT 1 FROM visit WHERE appointment_id=a.id) AND (@role='Регистратор' OR (@role='Пациент' AND a.patient_id=@p) OR (@role='Врач' AND sc.employee_id=@e))",("id",id),("role",session.Role),("p",session.PatientId??0),("e",session.EmployeeId??0));
                if(found is not int owner)throw new InvalidOperationException("Эту запись нельзя изменить.");patient=owner;
                await Exec(c,"UPDATE appointment SET status=@state WHERE id=@id",("state",action=="cancel"?"отменена":"неявка"),("id",id));
                await Notify(c,owner,"Статус приёма изменён","Запись №"+id+": "+(action=="cancel"?"отменена":"неявка"));break;
            case "start_visit":
                await CheckAsync(c,session,"Врач");table="visit";
                await Exec(c,"SELECT id FROM appointment WHERE id=@id FOR UPDATE",("id",id));
                var aPatient=await Scalar(c,"SELECT a.patient_id FROM appointment a JOIN schedule sc ON sc.id=a.schedule_id WHERE a.id=@id AND sc.employee_id=@e AND a.status='запланирована' AND sc.work_date<=current_date",("id",id),("e",session.EmployeeId));
                if(aPatient is not int ap)throw new InvalidOperationException("Приём недоступен или назначен на будущую дату.");patient=ap;
                var appointmentId=id;
                var inserted=await Scalar(c,"INSERT INTO visit(appointment_id,visited_at) VALUES(@id,now()) ON CONFLICT(appointment_id) DO NOTHING RETURNING id",("id",appointmentId));
                id=Convert.ToInt32(inserted??await Scalar(c,"SELECT id FROM visit WHERE appointment_id=@id",("id",appointmentId)));auditAction=inserted is null?"SELECT":"INSERT";break;
            case "close_visit":
                await CheckAsync(c,session,"Врач");table="visit";patient=await OwnVisit(c,session,id);
                if(await Scalar(c,"SELECT diagnosis_code FROM visit WHERE id=@id",("id",id)) is not string)throw new InvalidOperationException("Перед закрытием укажите диагноз.");
                await Exec(c,"UPDATE visit SET is_closed=true WHERE id=@id; UPDATE appointment SET status='завершена' WHERE id=(SELECT appointment_id FROM visit WHERE id=@id)",("id",id));break;
            case "cancel_referral":
                await CheckAsync(c,session,"Врач");table="referral";patient=await ReferralPatient(c,session,id);
                if(await Scalar(c,"SELECT id FROM referral WHERE id=@id AND status='выполнено'",("id",id)) is not null)throw new InvalidOperationException("Исследование уже выполнено.");
                await Exec(c,"UPDATE referral SET status='отменено' WHERE id=@id",("id",id));break;
            case "create_invoice":
                await CheckAsync(c,session,"Регистратор");table="invoice";
                // The selected unbilled service determines the patient; bill all their unbilled services atomically.
                patient=Convert.ToInt32(await Scalar(c,"SELECT a.patient_id FROM visit_service vs JOIN visit v ON v.id=vs.visit_id JOIN appointment a ON a.id=v.appointment_id WHERE vs.id=@id AND vs.invoice_id IS NULL",("id",id))??throw new InvalidOperationException("Услуга уже включена в счёт."));
                await Exec(c,"SELECT pg_advisory_xact_lock(904,@p)",("p",patient));
                if(await Scalar(c,"SELECT id FROM visit_service WHERE id=@id AND invoice_id IS NULL",("id",id)) is null)
                    throw new InvalidOperationException("Услуга уже включена в счёт. Обновите список.");
                var bill=await Scalar(c,"INSERT INTO invoice(patient_id) VALUES(@p) RETURNING id",("p",patient));
                await Exec(c,"UPDATE visit_service SET invoice_id=@bill WHERE invoice_id IS NULL AND visit_id IN(SELECT v.id FROM visit v JOIN appointment a ON a.id=v.appointment_id WHERE a.patient_id=@p)",("bill",bill),("p",patient));
                id=Convert.ToInt32(bill);auditAction="INSERT";await Notify(c,patient.Value,"Новый счёт","Счёт №"+id+" доступен в личном кабинете.");break;
            case "mark_read":
                await CheckAsync(c,session,"Пациент");table="notification";patient=session.PatientId;
                await Exec(c,"UPDATE notification SET is_read=true WHERE id=@id AND patient_id=@p",("id",id),("p",patient));break;
            case "toggle_patient":
            case "toggle_employee":
            case "toggle_service":
            case "toggle_account":
                table=action[7..];await CheckAsync(c,session,table=="patient"?"Регистратор":"Администратор");
                if(table=="account"&&id==session.AccountId)throw new InvalidOperationException("Нельзя заблокировать собственную учётную запись.");
                await Exec(c,$"UPDATE {table} SET is_active=NOT is_active WHERE id=@id",("id",id));break;
            default:throw new InvalidOperationException("Неизвестное действие.");
        }
        await Audit(c,session,auditAction,table,id,patient);await tx.CommitAsync();
    }
    public async Task RegisterAsync(Dictionary<string,string> values)
    {
        ValidateFields(new("registration",0,values));var password=values.GetValueOrDefault("password","");
        if(password.Length<8||Get(values,"login").Length<3)throw new InvalidOperationException("Логин от 3 символов, пароль от 8 символов.");
        await using var c=await OpenAsync();await using var tx=await c.BeginTransactionAsync();
        var patient=await Scalar(c,"SELECT id FROM patient WHERE oms_policy=@oms AND birth_date=@birth AND is_active",("oms",Get(values,"oms_policy")),("birth",Date(values,"birth_date")));
        if(patient is not int p)throw new InvalidOperationException("Пациент с такими данными не найден. Обратитесь в регистратуру.");
        await Exec(c,"INSERT INTO account(login,password_hash,role_id,patient_id) VALUES(@login,crypt(@secret,gen_salt('bf')),(SELECT id FROM role WHERE name='Пациент'),@p)",("login",Get(values,"login")),("secret",password),("p",p));await tx.CommitAsync();
    }
    public async Task<byte[]?> AttachmentAsync(ClinicSession session,int referralId)
    {
        await using var c=await OpenAsync();await CheckAsync(c,session,"Пациент","Врач");
        var data=await Scalar(c,"SELECT r.file_data FROM referral r JOIN visit v ON v.id=r.visit_id JOIN appointment a ON a.id=v.appointment_id JOIN schedule sc ON sc.id=a.schedule_id WHERE r.id=@id AND ((@role='Пациент' AND a.patient_id=@p) OR (@role='Врач' AND (sc.employee_id=@e OR r.consultation_doctor_id=@e)))",("id",referralId),("role",session.Role),("p",session.PatientId??0),("e",session.EmployeeId??0));
        await Audit(c,session,"SELECT","referral",referralId,session.PatientId);return data as byte[];
    }
}
