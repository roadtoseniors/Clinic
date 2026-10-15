using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Clinic.Workspace;
using Npgsql;

namespace Clinic.Data;

public sealed partial class ClinicDatabase
{
    internal async Task<NpgsqlConnection> OpenAsync()
    {
        var connection=new NpgsqlConnection(_connectionString);
        try { await connection.OpenAsync(); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
    internal static async Task CheckAsync(NpgsqlConnection c,ClinicSession s,params string[] roles)
    {
        var value=await Scalar(c,"SELECT r.name FROM account a JOIN role r ON r.id=a.role_id WHERE a.id=@id AND a.is_active AND a.patient_id IS NOT DISTINCT FROM @patient AND a.employee_id IS NOT DISTINCT FROM @employee",("id",s.AccountId),("patient",s.PatientId),("employee",s.EmployeeId));
        if(value is not string role || role!=s.Role || (roles.Length>0 && !roles.Contains(role)))
            throw new InvalidOperationException("Недостаточно прав или учётная запись заблокирована. Войдите заново.");
    }
    internal static NpgsqlCommand Command(NpgsqlConnection c,string sql,params (string Key,object? Value)[] values)
    {
        var cmd=new NpgsqlCommand(sql,c);
        foreach(var (key,value) in values) cmd.Parameters.AddWithValue(key,value??DBNull.Value);
        return cmd;
    }
    internal static async Task<object?> Scalar(NpgsqlConnection c,string sql,params (string Key,object? Value)[] values)
    {
        await using var cmd=Command(c,sql,values);return await cmd.ExecuteScalarAsync();
    }
    internal static async Task Exec(NpgsqlConnection c,string sql,params (string Key,object? Value)[] values)
    {
        await using var cmd=Command(c,sql,values);await cmd.ExecuteNonQueryAsync();
    }
    internal static async Task Audit(NpgsqlConnection c,ClinicSession s,string action,string table,int? id=null,int? patient=null)
        => await Exec(c,"INSERT INTO audit(account_id,patient_id,action,table_name,record_id) VALUES(@account,@patient,@action,@table,@id)",
            ("account",s.AccountId),("patient",patient),("action",action),("table",table),("id",id));
    internal static async Task Notify(NpgsqlConnection c,int patient,string title,string message)
        => await Exec(c,"INSERT INTO notification(patient_id,title,message) VALUES(@p,@t,@m)",("p",patient),("t",title),("m",message));
    public async Task<LiveDashboard> DashboardAsync(ClinicSession session)
    {
        await using var c=await OpenAsync();await CheckAsync(c,session);return await LoadDashboardAsync(c,session);
    }
    private const string PatientName="p.last_name||' '||p.first_name||COALESCE(' '||p.middle_name,'')";
    private const string EmployeeName="e.last_name||' '||e.first_name||COALESCE(' '||e.middle_name,'')";
    private static string Scope(ClinicSession s,string patientAlias="a",string scheduleAlias="s") => s.Role switch
    {
        "Пациент"=>$"{patientAlias}.patient_id=@patient",
        "Врач"=>$"{scheduleAlias}.employee_id=@employee",
        "Регистратор"=>"true",
        _=>"false"
    };
    public async Task<List<Record>> ListAsync(ClinicSession session,string page,DateTime? from=null,DateTime? to=null)
    {
        if(!Catalog.Sections(session.Role).Any(s=>s.Key==page)) throw new InvalidOperationException("Раздел недоступен.");
        await using var c=await OpenAsync();await CheckAsync(c,session);
        var scope=Scope(session);
        var periodStart=from??new DateTime(DateTime.Today.Year,DateTime.Today.Month,1);
        var periodEnd=to??DateTime.Today;
        if(periodEnd<periodStart)throw new InvalidOperationException("Начало периода должно быть раньше окончания.");
        var patientScope=session.Role=="Пациент"?"p.id=@patient":session.Role=="Врач"?
            "EXISTS(SELECT 1 FROM appointment ax JOIN schedule sx ON sx.id=ax.schedule_id WHERE ax.patient_id=p.id AND sx.employee_id=@employee)":"true";
        string sql=page switch
        {
            "patients"=>$"""
                SELECT p.*, {PatientName} c1,to_char(p.birth_date,'DD.MM.YYYY') c2,p.oms_policy c3,COALESCE(p.phone,'—') c4,
                  CASE WHEN p.is_active THEN 'активен' ELSE 'архив' END status,
                  'Карта: '||p.card_number||E'\nАдрес: '||COALESCE(p.address,'—')||E'\nСНИЛС: '||COALESCE(p.snils,'—')||
                  E'\nДМС: '||COALESCE(p.dms_policy,'—')||E'\nСтраховая: '||COALESCE(p.insurance_org,'—')||
                  E'\nУчасток: '||COALESCE(p.district,'—')||E'\nГруппа крови: '||COALESCE(p.blood_group,'—')||
                  E'\nАллергии: '||COALESCE(p.allergies,'—') details
                FROM patient p WHERE {patientScope} ORDER BY p.last_name,p.first_name
                """,
            "schedule"=>$"""
                SELECT s.*,to_char(s.work_date,'DD.MM.YYYY') c1,{EmployeeName} c2,
                to_char(s.start_time,'HH24:MI')||' — '||to_char(s.end_time,'HH24:MI') c3,rm.number c4,
                'слот '||s.slot_minutes||' мин' status,
                'Специальность: '||COALESCE(sp.name,'—')||E'\nКабинет: '||rm.number details
                FROM schedule s JOIN employee e ON e.id=s.employee_id JOIN room rm ON rm.id=s.room_id
                LEFT JOIN specialty sp ON sp.id=e.specialty_id
                WHERE {(session.Role=="Врач"?"s.employee_id=@employee":"true")} ORDER BY s.work_date DESC,s.start_time
                """,
            "slots"=>"""
                SELECT s.id, s.id schedule_id, to_char(slot,'HH24:MI') start_time,
                s.id||'|'||to_char(slot,'HH24:MI') slot,
                to_char(s.work_date,'DD.MM.YYYY')||' '||to_char(slot,'HH24:MI') c1,
                e.last_name||' '||e.first_name c2,sp.name c3,rm.number c4,'свободно' status,
                to_char(s.work_date,'YYYY-MM-DD') work_date, e.id employee_id,
                'Кабинет '||rm.number||' · '||s.slot_minutes||' минут' details
                FROM schedule s JOIN employee e ON e.id=s.employee_id JOIN specialty sp ON sp.id=e.specialty_id
                JOIN room rm ON rm.id=s.room_id
                CROSS JOIN LATERAL generate_series(s.work_date+s.start_time,s.work_date+s.end_time-s.slot_minutes*interval '1 minute',s.slot_minutes*interval '1 minute') slot
                WHERE e.is_active AND slot>localtimestamp AND s.work_date<=current_date+30
                AND NOT EXISTS(SELECT 1 FROM appointment a WHERE a.schedule_id=s.id AND a.start_time=slot::time AND a.status<>'отменена')
                ORDER BY s.work_date,slot,e.last_name
                """,
            "appointments"=>$"""
                SELECT a.*,s.work_date,s.employee_id,
                to_char(s.work_date,'DD.MM.YYYY')||' '||to_char(a.start_time,'HH24:MI') c1,
                {(session.Role=="Пациент"?EmployeeName:PatientName)} c2,
                {(session.Role=="Врач"?"p.card_number":session.Role=="Пациент"?"COALESCE(sp.name,'Специалист')":EmployeeName)} c3,
                rm.number c4,a.status,
                'Пациент: '||{PatientName}||E'\nВрач: '||{EmployeeName}||E'\nКабинет: '||rm.number||E'\nИсточник: '||a.source details
                FROM appointment a JOIN schedule s ON s.id=a.schedule_id JOIN patient p ON p.id=a.patient_id
                JOIN employee e ON e.id=s.employee_id LEFT JOIN specialty sp ON sp.id=e.specialty_id JOIN room rm ON rm.id=s.room_id
                WHERE {scope} ORDER BY s.work_date DESC,a.start_time
                """,
            "visits"=>$"""
                SELECT v.*,a.patient_id,to_char(v.visited_at,'DD.MM.YYYY HH24:MI') c1,{PatientName} c2,{EmployeeName} c3,
                COALESCE(v.diagnosis_code||' · '||d.name,'Не указан') c4,
                CASE WHEN v.is_closed THEN 'закрыт' ELSE 'в работе' END status,
                'Жалобы: '||COALESCE(v.complaints,'—')||E'\n\nАнамнез: '||COALESCE(v.anamnesis,'—')||
                E'\n\nОбъективный статус: '||COALESCE(v.objective_status,'—')||E'\n\nДиагноз: '||COALESCE(d.name,'—')||
                E'\n\nРекомендации: '||COALESCE(v.recommendations,'—') details
                FROM visit v JOIN appointment a ON a.id=v.appointment_id JOIN schedule s ON s.id=a.schedule_id
                JOIN patient p ON p.id=a.patient_id JOIN employee e ON e.id=s.employee_id LEFT JOIN diagnosis d ON d.code=v.diagnosis_code
                WHERE {scope} ORDER BY v.visited_at DESC
                """,
            "referrals"=>$"""
                SELECT r.id,r.visit_id,r.service_id,r.status,r.executor_id,r.done_at,r.result,r.file_name,r.consultation_doctor_id,a.patient_id,
                sv.name c1,{PatientName} c2,to_char(v.visited_at,'DD.MM.YYYY') c3,
                COALESCE(ex.last_name||' '||ex.first_name,'Не назначен') c4,
                'Результат: '||COALESCE(r.result,'Ожидается')||E'\nФайл: '||COALESCE(r.file_name,'—') details
                FROM referral r JOIN service sv ON sv.id=r.service_id JOIN visit v ON v.id=r.visit_id
                JOIN appointment a ON a.id=v.appointment_id JOIN schedule s ON s.id=a.schedule_id JOIN patient p ON p.id=a.patient_id
                LEFT JOIN employee ex ON ex.id=r.executor_id
                WHERE {(session.Role=="Врач"?$"({scope} OR r.consultation_doctor_id=@employee)":scope)} ORDER BY r.id DESC
                """,
            "prescriptions"=>$"""
                SELECT pr.*,a.patient_id,d.name c1,{PatientName} c2,COALESCE(pr.dosage,'—') c3,{EmployeeName} c4,
                CASE WHEN pr.is_recipe THEN 'рецепт' ELSE 'назначение' END status,
                COALESCE(pr.instruction,'—')||E'\nДействует до: '||COALESCE(to_char(pr.valid_until,'DD.MM.YYYY'),'—') details
                FROM prescription pr JOIN drug d ON d.id=pr.drug_id JOIN visit v ON v.id=pr.visit_id
                JOIN appointment a ON a.id=v.appointment_id JOIN schedule s ON s.id=a.schedule_id
                JOIN patient p ON p.id=a.patient_id JOIN employee e ON e.id=s.employee_id WHERE {scope} ORDER BY pr.id DESC
                """,
            "rendered"=>$"""
                SELECT vs.*,a.patient_id,sv.name c1,{PatientName} c2,to_char(vs.rendered_at,'DD.MM.YYYY') c3,
                to_char(vs.price*vs.quantity,'FM999999990.00')||' ₽' c4,
                CASE WHEN vs.invoice_id IS NULL THEN 'без счёта' ELSE 'счёт №'||vs.invoice_id END status,
                'Количество: '||vs.quantity||E'\nЦена за единицу: '||vs.price details
                FROM visit_service vs JOIN service sv ON sv.id=vs.service_id JOIN visit v ON v.id=vs.visit_id
                JOIN appointment a ON a.id=v.appointment_id JOIN patient p ON p.id=a.patient_id ORDER BY vs.id DESC
                """,
            "invoices"=>$"""
                SELECT i.*,i.total::text total_text,COALESCE((SELECT sum(amount) FROM payment WHERE invoice_id=i.id),0)::text paid,
                'Счёт №'||i.id c1,{PatientName} c2,i.total::text||' ₽' c3,
                COALESCE((SELECT sum(amount) FROM payment WHERE invoice_id=i.id),0)::text||' ₽' c4,i.status,
                COALESCE((SELECT string_agg(sv.name||' × '||vs.quantity||' = '||(vs.price*vs.quantity)||' ₽',E'\n') FROM visit_service vs JOIN service sv ON sv.id=vs.service_id WHERE vs.invoice_id=i.id),'Нет услуг')||
                E'\n\nПлатежи:\n'||COALESCE((SELECT string_agg(to_char(pm.created_at,'DD.MM.YYYY HH24:MI')||' · '||m.name||' · '||pm.amount||' ₽',E'\n') FROM payment pm JOIN pay_method m ON m.id=pm.method_id WHERE pm.invoice_id=i.id),'Нет платежей') details
                FROM v_invoice i JOIN patient p ON p.id=i.patient_id WHERE {(session.Role=="Пациент"?"i.patient_id=@patient":"true")} ORDER BY i.id DESC
                """,
            "documents"=>$"""
                SELECT doc.*,doc.kind c1,{PatientName} c2,to_char(doc.created_at,'DD.MM.YYYY') c3,ac.login c4,'оформлен' status,doc.content details
                FROM patient_document doc JOIN patient p ON p.id=doc.patient_id JOIN account ac ON ac.id=doc.account_id
                WHERE {patientScope} ORDER BY doc.id DESC
                """,
            "notifications"=>"""
                SELECT n.*,n.title c1,to_char(n.created_at,'DD.MM.YYYY HH24:MI') c2,n.message c3,'' c4,
                CASE WHEN n.is_read THEN 'прочитано' ELSE 'новое' END status,n.message details
                FROM notification n WHERE patient_id=@patient ORDER BY id DESC
                """,
            "accounts"=>"""
                SELECT ac.id,ac.login,ac.role_id,ac.employee_id,ac.patient_id,ac.permissions,ac.login c1,
                COALESCE(e.last_name||' '||e.first_name,p.last_name||' '||p.first_name) c2,r.name c3,
                COALESCE(to_char(ac.last_login,'DD.MM.YYYY HH24:MI'),'—') c4,
                CASE WHEN ac.is_active THEN 'активен' ELSE 'заблокирован' END status,'Роль: '||r.name details
                FROM account ac JOIN role r ON r.id=ac.role_id LEFT JOIN employee e ON e.id=ac.employee_id LEFT JOIN patient p ON p.id=ac.patient_id
                WHERE r.name<>'Лаборант' ORDER BY ac.id
                """,
            "employees"=>$"""
                SELECT e.*,{EmployeeName} c1,post.name c2,COALESCE(sp.name,'—') c3,COALESCE(e.phone,'—') c4,
                CASE WHEN e.is_active THEN 'активен' ELSE 'архив' END status,post.name details
                FROM employee e JOIN post ON post.id=e.post_id LEFT JOIN specialty sp ON sp.id=e.specialty_id ORDER BY e.last_name
                """,
            "services"=>"""
                SELECT sv.*,sv.name c1,g.name c2,sv.price::text||' ₽' c3,'' c4,
                CASE WHEN sv.is_active THEN 'активна' ELSE 'архив' END status,g.name details FROM service sv JOIN service_group g ON g.id=sv.group_id ORDER BY g.name,sv.name
                """,
            "audit"=>"""
                SELECT au.id,au.patient_id,to_char(au.acted_at,'DD.MM.YYYY HH24:MI') c1,ac.login c2,au.action c3,
                au.table_name||COALESCE(' №'||au.record_id,'') c4,'аудит' status,
                'Пациент: '||COALESCE(au.patient_id::text,'—') details FROM audit au JOIN account ac ON ac.id=au.account_id ORDER BY au.id DESC LIMIT 2000
                """,
            "dictionary"=>"""
                SELECT id,'specialty' "table",'' code,name,'Специальности' c1,id::text c2,name c3,'' c4,'справочник' status,name details FROM specialty
                UNION ALL SELECT id,'post','',name,'Должности',id::text,name,'','справочник',name FROM post
                UNION ALL SELECT id,'service_group','',name,'Категории услуг',id::text,name,'','справочник',name FROM service_group
                UNION ALL SELECT id,'drug','',name,'Лекарства',id::text,name,'','справочник',name FROM drug
                UNION ALL SELECT id,'room',floor::text,number,'Кабинеты',id::text,number,'','справочник','Этаж '||floor FROM room
                UNION ALL SELECT 0,'diagnosis',code,name,'МКБ-10',code,name,'','справочник',name FROM diagnosis ORDER BY c1,c3
                """,
            "reports"=>"""
                SELECT 1 id,'Посещения за период' c1,count(*)::text c2,to_char(@from,'DD.MM.YYYY')||' — '||to_char(@to,'DD.MM.YYYY') c3,'' c4,'отчёт' status,'' details FROM visit WHERE visited_at>=@from AND visited_at<@to+interval '1 day'
                UNION ALL SELECT 2,'Выручка за период',COALESCE(sum(amount),0)::text||' ₽',to_char(@from,'DD.MM.YYYY')||' — '||to_char(@to,'DD.MM.YYYY'),'','отчёт','' FROM payment WHERE created_at>=@from AND created_at<@to+interval '1 day'
                UNION ALL SELECT 3,'Неоплаченные счета',count(*)::text,'Все даты','','отчёт','' FROM invoice WHERE status='не оплачен'
                UNION ALL SELECT 100+e.id,'Загрузка: '||e.last_name,
                  round(100.0*COALESCE((SELECT count(*) FROM appointment a JOIN schedule sc ON sc.id=a.schedule_id WHERE sc.employee_id=e.id AND sc.work_date BETWEEN @from AND @to AND a.status<>'отменена'),0)/
                    GREATEST(COALESCE((SELECT sum(EXTRACT(epoch FROM(sc.end_time-sc.start_time))/60/sc.slot_minutes) FROM schedule sc WHERE sc.employee_id=e.id AND sc.work_date BETWEEN @from AND @to),0),1),1)::text||' %',
                  'Выбранный период','','отчёт',COALESCE(sp.name,'')
                  FROM employee e LEFT JOIN specialty sp ON sp.id=e.specialty_id WHERE EXISTS(SELECT 1 FROM schedule sc WHERE sc.employee_id=e.id AND sc.work_date BETWEEN @from AND @to)
                UNION ALL SELECT 200+row_number() OVER()::integer,'Диагноз: '||d.code,count(v.id)::text,'Выбранный период','','отчёт',d.name FROM diagnosis d JOIN visit v ON v.diagnosis_code=d.code WHERE v.visited_at>=@from AND v.visited_at<@to+interval '1 day' GROUP BY d.code ORDER BY id
                """,
            _=>"SELECT 0 id,'' c1,'' c2,'' c3,'' c4,'' status,'' details WHERE false"
        };
        await using var cmd=Command(c,sql,("patient",session.PatientId??0),("employee",session.EmployeeId??0),("from",periodStart),("to",periodEnd));
        var records=new List<Record>();
        await using(var reader=await cmd.ExecuteReaderAsync())
        {
            while(await reader.ReadAsync())
            {
                var v=new Dictionary<string,string>();
                for(int i=0;i<reader.FieldCount;i++)
                    v[reader.GetName(i)]=reader.IsDBNull(i)?"":Format(reader.GetValue(i));
                records.Add(new Record { Id=int.Parse(v["id"],CultureInfo.InvariantCulture),Cell1=v["c1"],Cell2=v["c2"],Cell3=v["c3"],Cell4=v["c4"],Status=v["status"],Details=v["details"],Values=v });
            }
        }
        if(page is "patients" or "visits" or "referrals" or "prescriptions") await Audit(c,session,"SELECT",page,patient:session.PatientId);
        return records;
    }
    private static string Format(object value)=>value switch
    {
        DateTime d=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        DateOnly d=>d.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),
        TimeSpan t=>t.ToString(@"hh\:mm",CultureInfo.InvariantCulture),
        TimeOnly t=>t.ToString("HH:mm",CultureInfo.InvariantCulture),
        bool b=>b?"true":"false",
        _=>Convert.ToString(value,CultureInfo.InvariantCulture)??""
    };
    public async Task<Dictionary<string,List<Choice>>> ChoicesAsync(ClinicSession session)
    {
        await using var c=await OpenAsync();await CheckAsync(c,session);
        var result=new Dictionary<string,List<Choice>>();
        var queries=new Dictionary<string,string>
        {
            ["doctors"]="SELECT e.id,e.last_name||' '||e.first_name||' · '||sp.name name FROM employee e JOIN specialty sp ON sp.id=e.specialty_id WHERE e.is_active AND EXISTS(SELECT 1 FROM account ac JOIN role r ON r.id=ac.role_id WHERE ac.employee_id=e.id AND r.name='Врач') ORDER BY e.last_name",
            ["patients"]=$"SELECT p.id,{PatientName} name FROM patient p WHERE p.is_active AND "+(session.Role=="Пациент"?"p.id=@patient":session.Role=="Врач"?"EXISTS(SELECT 1 FROM appointment a JOIN schedule s ON s.id=a.schedule_id WHERE a.patient_id=p.id AND s.employee_id=@employee)":"true")+" ORDER BY p.last_name",
            ["rooms"]="SELECT id,number name FROM room ORDER BY number",
            ["diagnoses"]="SELECT code id,code||' · '||name name FROM diagnosis ORDER BY code",
            ["drugs"]="SELECT id,name FROM drug ORDER BY name",
            ["services"]="SELECT id,name||' · '||price||' ₽' name FROM service WHERE is_active ORDER BY name",
            ["roles"]="SELECT id,name FROM role WHERE name IN('Пациент','Регистратор','Врач','Администратор') ORDER BY id",
            ["employees"]="SELECT id,last_name||' '||first_name name FROM employee WHERE is_active ORDER BY last_name",
            ["posts"]="SELECT id,name FROM post ORDER BY name",
            ["specialties"]="SELECT id,name FROM specialty ORDER BY name",
            ["groups"]="SELECT id,name FROM service_group ORDER BY name",
            ["methods"]="SELECT id,name FROM pay_method ORDER BY id",
            ["visits"]=$"SELECT v.id,{PatientName}||' · '||to_char(v.visited_at,'DD.MM.YYYY') name FROM visit v JOIN appointment a ON a.id=v.appointment_id JOIN schedule s ON s.id=a.schedule_id JOIN patient p ON p.id=a.patient_id WHERE {Scope(session)} ORDER BY v.id DESC",
            ["open_visits"]=$"SELECT v.id,{PatientName}||' · '||to_char(v.visited_at,'DD.MM.YYYY') name FROM visit v JOIN appointment a ON a.id=v.appointment_id JOIN schedule s ON s.id=a.schedule_id JOIN patient p ON p.id=a.patient_id WHERE {Scope(session)} AND NOT v.is_closed ORDER BY v.id DESC"
        };
        // An administrator doesn't receive patient names or medical visit lists.
        if(session.Role=="Администратор") { queries.Remove("visits");queries.Remove("open_visits"); }
        foreach(var (key,sql) in queries)
        {
            await using var cmd=Command(c,sql,("patient",session.PatientId??0),("employee",session.EmployeeId??0));
            await using var reader=await cmd.ExecuteReaderAsync();var list=new List<Choice>();
            while(await reader.ReadAsync()) list.Add(new Choice(Format(reader.GetValue(0)),reader.GetString(1)));
            result[key]=list;
        }
        result["gender"]=[new("М","Мужской"),new("Ж","Женский")];
        result["boolean"]=[new("false","Нет"),new("true","Да")];
        result["document_types"]=session.Role=="Врач"?[new("Справка","Справка")]:[new("Согласие на обработку данных","Согласие на обработку данных"),new("Согласие на вмешательство","Согласие на вмешательство"),new("Договор платных услуг","Договор платных услуг")];
        result["dictionary_types"]=[new("specialty","Специальности"),new("post","Должности"),new("room","Кабинеты"),new("service_group","Категории услуг"),new("drug","Лекарства"),new("diagnosis","МКБ-10")];
        if(session.Role is "Пациент" or "Регистратор") result["slots"]=(await ListAsync(session,"slots")).Select(r=>new Choice(r.Values["slot"],r.Cell1+" · "+r.Cell2+" · "+r.Cell3)).ToList();
        return result;
    }
}
