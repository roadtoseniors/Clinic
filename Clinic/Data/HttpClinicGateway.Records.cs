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
using Clinic.Workspace;

namespace Clinic.Data;

public sealed partial class HttpClinicGateway
{
    private async Task<List<JsonElement>> Patients(ClinicSession s)
    {
        if (s.Role == "Пациент")
            return Permissions.Contains("own.view") ? [Property(await Request("GET", "api/my/profile"), "patient")] : [];
        return Permissions.Contains("patient.view") ? await Many("api/patients?includeInactive=true") : [];
    }

    private async Task<List<JsonElement>> Visits(ClinicSession s)
    {
        if (s.Role == "Пациент")
            return Items(Property(await Request("GET", "api/my/medcard"), "visits"));
        var summaries = await Many("api/visits");
        if (s.Role != "Врач")
            return summaries;
        var result = new List<JsonElement>();
        foreach (var v in summaries)
            result.Add(await Request("GET", "api/visits/" + Integer(v, "id")));
        return result;
    }

    public async Task<List<Record>> ListAsync(ClinicSession s, string page, DateTime? from = null, DateTime? to = null)
    {
        Check(s);
        if (!(page == "roles" && Permissions.Contains("role.manage")) && !Catalog.Sections(s.Role).Any(p => p.Key == page))
            throw new InvalidOperationException("Раздел недоступен.");
        var rows = new List<Record>();
        switch (page)
        {
            case "patients":
                foreach (var p in await Patients(s))
                {
                    var profile = await Request("GET", s.Role == "Пациент" ? "api/my/profile" : "api/patients/" + Integer(p, "id"));
                    var policies = Items(Property(profile, "policies"));
                    var oms = policies.FirstOrDefault(x => Text(x, "kind") == "ОМС" && Boolean(x, "isActive"));
                    var dms = policies.FirstOrDefault(x => Text(x, "kind") == "ДМС" && Boolean(x, "isActive"));
                    var attachment = Property(profile, "attachment");
                    var extra = new Dictionary<string, string>
                    {
                        ["oms_policy"] = Text(oms, "number"),
                        ["oms_company_id"] = Text(oms, "companyId"),
                        ["dms_policy"] = Text(dms, "number"),
                        ["dms_company_id"] = Text(dms, "companyId"),
                        ["district_id"] = Text(attachment, "districtId"),
                        ["primary_doctor_id"] = Text(attachment, "employeeId")
                    };
                    rows.Add(Row(p, FullName(p), DisplayDate(Text(p, "birthDate")), Text(oms, "number"), Text(p, "phone"), Boolean(p, "isActive") ? "активен" : "архив", "Карта: " + Text(p, "cardNumber") + "\nАдрес: " + Text(p, "address") + "\nСНИЛС: " + Text(p, "snils") + "\nОМС: " + Text(oms, "number") + " · " + Text(oms, "companyName") + "\nДМС: " + Text(dms, "number") + "\nУчасток: " + Text(attachment, "districtNumber") + "\nУчастковый врач: " + Text(attachment, "doctorName"), extra));
                }

                break;
            case "schedule":
                foreach (var v in await Many("api/schedules?from=" + (from ?? DateTime.Today.AddDays(-30)).ToString("yyyy-MM-dd") + "&to=" + (to ?? DateTime.Today.AddDays(30)).ToString("yyyy-MM-dd") + (s.Role == "Врач" ? "&employeeId=" + s.EmployeeId : "")))
                    rows.Add(Row(v, DisplayDate(Text(v, "workDate")), Text(v, "doctorName"), Time(v, "startTime") + " — " + Time(v, "endTime"), Text(v, "roomNumber"), "слот " + Text(v, "slotMinutes") + " мин", extra: new() { ["start_time"] = Time(v, "startTime"), ["end_time"] = Time(v, "endTime") }));
                break;
            case "slots":
                var doctors = (await Many("api/doctors")).ToDictionary(v => Integer(v, "id"));
                var begin = from ?? DateTime.Today;
                var end = to ?? begin.AddDays(14);
                if (end < begin || (end - begin).TotalDays > 31)
                    throw new InvalidOperationException("Выберите период слотов не больше 31 дня.");
                for (var day = begin.Date; day <= end.Date; day = day.AddDays(1))
                    foreach (var v in await Many("api/slots?date=" + day.ToString("yyyy-MM-dd")))
                    {
                        doctors.TryGetValue(Integer(v, "employeeId"), out var doctor);
                        var key = Integer(v, "scheduleId") + "|" + Time(v, "startTime");
                        rows.Add(Row(v, DisplayDate(Text(v, "workDate")) + " " + Time(v, "startTime"), Text(v, "doctorName"), Text(doctor, "specialtyName"), "", "свободно", "Свободное время", new() { ["id"] = (Integer(v, "scheduleId") * 1440 + (int)TimeSpan.Parse(Time(v, "startTime")).TotalMinutes).ToString(), ["slot"] = key, ["start_time"] = Time(v, "startTime") }));
                    }

                break;
            case "appointments":
                if (s.Role == "Врач")
                {
                    foreach (var day in await Many("api/doctor/schedule?from=" + (from ?? DateTime.Today.AddDays(-30)).ToString("yyyy-MM-dd") + "&to=" + (to ?? DateTime.Today.AddDays(30)).ToString("yyyy-MM-dd")))
                        foreach (var v in Items(Property(day, "appointments")))
                            rows.Add(Row(v, DisplayDate(Text(day, "workDate")) + " " + Time(v, "startTime"), Text(v, "patientName"), Text(v, "cardNumber"), Text(day, "roomNumber"), Text(v, "status"), "Пациент: " + Text(v, "patientName"), new() { ["work_date"] = Text(day, "workDate"), ["schedule_id"] = Text(day, "scheduleId") }));
                }
                else
                    foreach (var v in await Many(s.Role == "Пациент" ? "api/my/appointments" : "api/appointments"))
                        rows.Add(Row(v, DisplayDate(Text(v, "workDate")) + " " + Time(v, "startTime"), s.Role == "Пациент" ? Text(v, "doctorName") : Text(v, "patientName"), s.Role == "Пациент" ? Text(v, "specialty") : Text(v, "doctorName"), Text(v, "room"), Text(v, "status"), "Врач: " + Text(v, "doctorName") + "\nИсточник: " + Text(v, "source"), new() { ["patient_id"] = (s.PatientId ?? Integer(v, "patientId")).ToString() }));
                break;
            case "visits":
            case "prescriptions":
            case "rendered":
                var names = (await Patients(s)).ToDictionary(p => Integer(p, "id"), FullName);
                foreach (var visit in await Visits(s))
                {
                    var patient = Integer(visit, "patientId");
                    var name = names.GetValueOrDefault(patient, Text(visit, "patientName"));
                    if (page == "visits")
                        rows.Add(Row(visit, DisplayDate(Text(visit, "visitedAt")), name, Text(visit, "doctorName"), Text(visit, "diagnosisCode"), Boolean(visit, "isClosed") ? "закрыт" : "в работе", "Жалобы: " + Text(visit, "complaints") + "\nАнамнез: " + Text(visit, "anamnesis") + "\nОсмотр: " + Text(visit, "objectiveStatus") + "\nДиагноз: " + Text(visit, "diagnosisName") + "\nРекомендации: " + Text(visit, "recommendations")));
                    if (page == "prescriptions")
                        foreach (var v in Items(Property(visit, "prescriptions")))
                            rows.Add(Row(v, Text(v, "drugName"), name, Text(v, "dosage"), Text(visit, "doctorName"), Boolean(v, "isRecipe") ? "рецепт" : "назначение", Text(v, "instruction"), new() { ["patient_id"] = patient.ToString(), ["can_edit"] = (!Boolean(visit, "isClosed")).ToString().ToLowerInvariant(), ["can_delete"] = (!Boolean(visit, "isClosed")).ToString().ToLowerInvariant() }));
                    if (page == "rendered")
                        foreach (var v in await Many("api/visits/" + Integer(visit, "id") + "/services"))
                            rows.Add(Row(v, Text(v, "serviceName"), name, DisplayDate(Text(v, "renderedAt")), Money(v, "sum"), Integer(v, "invoiceId") == 0 ? "без счёта" : "счёт №" + Text(v, "invoiceId"), "Количество: " + Text(v, "quantity"), new() { ["patient_id"] = patient.ToString(), ["can_edit"] = "true" }));
                }

                break;
            case "referrals":
                foreach (var v in await Many(s.Role == "Пациент" ? "api/my/referrals" : "api/referrals?status=all"))
                {
                    var files = Integer(v, "fileCount") > 0 ? await Many(s.Role == "Пациент" ? "api/my/referrals/" + Integer(v, "id") + "/files" : "api/referrals/" + Integer(v, "id") + "/files") : [];
                    var file = files.FirstOrDefault();
                    rows.Add(Row(v, Text(v, "serviceName"), Text(v, "patientName"), DisplayDate(Text(v, "doneAt")), Text(v, "executorName"), Text(v, "status"), "Результат: " + Text(v, "result") + "\nФайлы: " + string.Join(", ", files.Select(f => Text(f, "fileName"))), new() { ["file_name"] = Text(file, "fileName"), ["file_id"] = Text(file, "id") }));
                }

                break;
            case "invoices":
                foreach (var v in await Many(s.Role == "Пациент" ? "api/my/invoices" : "api/invoices"))
                {
                    var paid = Text(v, "status") == "оплачен" ? Text(v, "total") : "0";
                    rows.Add(Row(v, "Счёт №" + Integer(v, "id"), Text(v, "patientName"), Money(v, "total"), paid + " ₽", Text(v, "status"), "Создан: " + DisplayDate(Text(v, "createdAt")) + "\nСпособ оплаты: " + Text(v, "methodName"), new() { ["total_text"] = Text(v, "total"), ["paid"] = paid }));
                }

                break;
            case "documents":
                foreach (var v in await Many(s.Role == "Пациент" ? "api/my/documents" : "api/documents"))
                    rows.Add(Row(v, Text(v, "typeName") + " · " + Text(v, "number"), Text(v, "patientName"), DisplayDate(Text(v, "issuedAt")), "", "оформлен", Text(v, "content"), new() { ["created_at"] = Text(v, "issuedAt") }));
                break;
            case "notifications":
                foreach (var v in await Many("api/my/notifications"))
                    rows.Add(Row(v, "Уведомление", DisplayDate(Text(v, "createdAt")), Text(v, "message"), "", Boolean(v, "isRead") ? "прочитано" : "новое", Text(v, "message")));
                break;
            case "roles":
                foreach (var role in await Many("api/roles"))
                    if (Text(role, "code") != "lab")
                        rows.Add(Row(role, Text(role, "code"), Text(role, "name"), string.Join(", ", Items(Property(role, "permissions")).Select(p => Catalog.PermissionName(p.ToString()))), "", "роль"));
                break;
            case "accounts":
                foreach (var v in await Many("api/accounts"))
                    if (Text(v, "roleName") != "Лаборант")
                        rows.Add(Row(v, Text(v, "login"), Text(v, "ownerName"), Text(v, "roleName"), DisplayDate(Text(v, "lastLogin")), Boolean(v, "isActive") ? "активен" : "заблокирован", "Роль: " + Text(v, "roleName")));
                break;
            case "employees":
                foreach (var v in await Many("api/employees?includeInactive=true"))
                    rows.Add(Row(v, FullName(v), Text(v, "postName"), Text(v, "specialtyName"), Text(v, "phone"), Boolean(v, "isActive") ? "активен" : "архив"));
                break;
            case "services":
                foreach (var v in await Many("api/services?includeInactive=true"))
                    rows.Add(Row(v, Text(v, "name"), Text(v, "groupName"), Money(v, "price"), "", Boolean(v, "isActive") ? "активна" : "архив"));
                break;
            case "dictionary":
                foreach (var(table, route)in DictionaryRoutes)
                    foreach (var v in await Many("api/" + route))
                        rows.Add(Row(v, DictionaryLabel(table), table == "diagnosis" ? Text(v, "code") : Text(v, "id"), table == "room" ? Text(v, "number") : Text(v, "name"), "", "справочник", extra: new() { ["table"] = table, ["name"] = table == "room" ? Text(v, "number") : Text(v, "name"), ["code"] = table == "room" ? Text(v, "floor") : Text(v, "code") }));
                break;
            case "audit":
                foreach (var v in await Many("api/audits"))
                    rows.Add(Row(v, Text(v, "actedAt"), Text(v, "login"), Text(v, "action"), Text(v, "tableName") + " №" + Text(v, "recordId"), "аудит", "Пациент: " + Text(v, "patientId")));
                break;
            case "reports":
                var range = "?from=" + (from ?? DateTime.Today.AddDays(-30)).ToString("yyyy-MM-dd") + "&to=" + (to ?? DateTime.Today).ToString("yyyy-MM-dd");
                var id = 0;
                foreach (var kind in new[]
                {
                    "attendance",
                    "load",
                    "morbidity",
                    "revenue"
                }

                )
                    foreach (var v in await Many("api/reports/" + kind + range))
                    {
                        var c1 = kind switch
                        {
                            "attendance" => "Посещаемость: " + DisplayDate(Text(v, "date")),
                            "load" => "Загрузка: " + Text(v, "doctorName"),
                            "morbidity" => "Диагноз: " + Text(v, "diagnosisCode"),
                            _ => "Выручка: " + DisplayDate(Text(v, "date"))};
                        var c2 = kind switch
                        {
                            "attendance" => Text(v, "total"),
                            "load" => Text(v, "loadPercent") + " %",
                            "morbidity" => Text(v, "visits"),
                            _ => Money(v, "amount")};
                        rows.Add(Row(v, c1, c2, DisplayDate((from ?? DateTime.Today.AddDays(-30)).ToString("yyyy-MM-dd")) + " — " + DisplayDate((to ?? DateTime.Today).ToString("yyyy-MM-dd")), "", "отчёт", v.ToString(), new() { ["id"] = (++id).ToString(), ["report_kind"] = kind }));
                    }

                break;
        }

        return rows;
    }

    private static readonly Dictionary<string, string> DictionaryRoutes = new()
    {
        ["specialty"] = "specialties",
        ["post"] = "posts",
        ["room"] = "rooms",
        ["service_group"] = "servicegroups",
        ["drug"] = "drugs",
        ["diagnosis"] = "diagnoses",
        ["district"] = "districts",
        ["insurance_company"] = "insurancecompanies",
        ["document_type"] = "documenttypes",
        ["pay_method"] = "paymethods"
    };
    private static string DictionaryLabel(string key) => key switch
    {
        "specialty" => "Специальности",
        "post" => "Должности",
        "room" => "Кабинеты",
        "service_group" => "Категории услуг",
        "drug" => "Лекарства",
        "diagnosis" => "МКБ-10",
        "district" => "Участки",
        "insurance_company" => "Страховые",
        "document_type" => "Документы",
        _ => "Способы оплаты"
    };
}
