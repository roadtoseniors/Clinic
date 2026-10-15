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
    public IReadOnlyList<Record> BookingSlots { get; private set; } = [];

    private int _editorId;
    private string _editorKind = "";
    private FieldSpec[] _permissionFields = [];
    public void PrepareEditor(string kind, int id)
    {
        _editorKind = kind;
        _editorId = id;
    }

    public FieldSpec[] FieldsFor(string kind, int id = 0) => kind switch
    {
        "role_permissions" => _permissionFields,
        "booking" when _session?.Role == "Пациент" => Catalog.Fields(kind).Where(f => f.Key != "patient_id").ToArray(),
        "rendered" when id > 0 => Catalog.Fields(kind).Where(f => f.Key != "visit_id").ToArray(),
        "patient" => Catalog.Fields("patient").Where(f => new[] { "last_name", "first_name", "middle_name", "birth_date", "gender", "oms_policy", "snils", "dms_policy", "phone", "address", "primary_doctor_id" }.Contains(f.Key)).Concat([new("oms_company_id", "Страховая ОМС", true, "companies"), new("dms_company_id", "Страховая ДМС", Lookup: "companies"), new("district_id", "Участок", Lookup: "districts")]).ToArray(),
        "prescription" => Catalog.Fields(kind).Where(f => f.Key != "valid_until" && (id == 0 || f.Key != "visit_id")).ToArray(),
        "referral" => Catalog.Fields(kind).Where(f => f.Key != "consultation_doctor_id").ToArray(),
        "payment" => [new("method_id", "Способ оплаты (полная сумма счёта)", true, "methods"), new("policy_id", "Полис, только при оплате по ДМС", Lookup: "policies")],
        "document" => [new("patient_id", "Пациент", true, "patients"), new("type_id", "Вид документа", true, "document_types"), new("visit_id", "Приём (обязательно для справки врача)", Lookup: "visits"), new("content", "Текст документа", true, Multiline: true)],
        "account" when id == 0 => Catalog.Fields(kind).Select(f => f.Key == "password" ? f with { Label = "Пароль (от 8 символов)", Required = true } : f).ToArray(),
        "account" when id > 0 => [new("role_id", "Роль", true, "roles"), new("password", "Новый пароль (пусто — оставить текущий)", Secret: true)],
        "registration" => [new("last_name", "Фамилия", true), new("first_name", "Имя", true), new("middle_name", "Отчество"), new("birth_date", "Дата рождения (ГГГГ-ММ-ДД)", true, Default: null), new("gender", "Пол", true, "gender"), new("snils", "СНИЛС"), new("phone", "Телефон"), new("address", "Адрес"), new("oms_policy", "Полис ОМС", true), new("oms_company_id", "Страховая ОМС", true, "companies"), new("login", "Логин", true), new("password", "Пароль (от 8 символов)", true, Secret: true), new("password_confirmation", "Подтверждение пароля", true, Secret: true)],
        _ => Catalog.Fields(kind)};
    public async Task<Dictionary<string, List<Choice>>> RegistrationChoicesAsync() => new()
    {
        ["gender"] = [new("М", "Мужской"), new("Ж", "Женский")],
        ["companies"] = (await Many("auth/insurancecompanies")).Select(c => new Choice(Text(c, "id"), Text(c, "name"))).ToList()
    };
    public async Task<Dictionary<string, List<Choice>>> ChoicesAsync(ClinicSession s)
    {
        Check(s);
        var result = new Dictionary<string, List<Choice>>();
        var needed = FieldsFor(_editorKind, _editorId).Where(f => f.Lookup is not null).Select(f => f.Lookup).ToHashSet();
        bool Needs(string key) => _editorKind.Length == 0 || needed.Contains(key);
        if (_editorKind == "booking")
            needed.Add("doctors");
        foreach (var (key, route) in new[]
        {
            ("doctors", "doctors"),
            ("rooms", "rooms"),
            ("diagnoses", "diagnoses"),
            ("drugs", "drugs"),
            ("services", "services"),
            ("posts", "posts"),
            ("specialties", "specialties"),
            ("groups", "servicegroups"),
            ("methods", "paymethods"),
            ("companies", "insurancecompanies"),
            ("districts", "districts"),
            ("document_types", "documenttypes")
        }

        )
            if (Needs(key))
                result[key] = (await Many("api/" + route)).Select(v => new Choice(route == "diagnoses" ? Text(v, "code") : Text(v, "id"), route == "rooms" ? Text(v, "number") : route == "doctors" ? Text(v, "fullName") : route == "diagnoses" ? Text(v, "code") + " · " + Text(v, "name") : Text(v, "name"))).ToList();
        if (s.Role == "Администратор" && (Needs("roles") || Needs("employees") || _editorKind == "role_permissions"))
        {
            result["roles"] = (await Many("api/roles")).Where(v => new[] { "patient", "registrar", "doctor", "admin", "administrator" }.Contains(Text(v, "code"))).Select(v => new Choice(Text(v, "id"), Text(v, "name"))).ToList();
            if (_editorKind == "role_permissions")
            {
                var role = (await Many("api/roles")).First(r => Integer(r, "id") == _editorId);
                var enabled = Items(Property(role, "permissions")).Select(p => p.ToString()).ToHashSet();
                _permissionFields = (await Many("api/permissions")).Select(p => new FieldSpec("perm:" + Text(p, "code"), Catalog.PermissionName(Text(p, "code")), true, "boolean", Default: enabled.Contains(Text(p, "code")) ? "true" : "false")).ToArray();
            }

            result["employees"] = (await Many("api/employees")).Select(v => new Choice(Text(v, "id"), FullName(v))).ToList();
        }

        if (Needs("patients") && (s.Role != "Администратор" || _editorKind == "account"))
            result["patients"] = (await Patients(s)).Where(p => Boolean(p, "isActive")).Select(p => new Choice(Text(p, "id"), FullName(p))).ToList();
        if (s.Role is "Врач" or "Регистратор" && (Needs("visits") || Needs("open_visits")))
        {
            var visits = await Visits(s);
            result["visits"] = visits.Select(v => new Choice(Text(v, "id"), (Text(v, "patientName").Length > 0 ? Text(v, "patientName") : "Пациент №" + Text(v, "patientId")) + " · " + DisplayDate(Text(v, "visitedAt")))).ToList();
            result["open_visits"] = result["visits"].Where(c => !Boolean(visits.First(v => Integer(v, "id").ToString() == c.Id), "isClosed")).ToList();
        }

        if (s.Role is "Пациент" or "Регистратор" && Needs("slots"))
        {
            BookingSlots = await ListAsync(s, "slots");
            result["slots"] = BookingSlots.Select(r => new Choice(r.Values["slot"], r.Cell1 + " · " + r.Cell2)).ToList();
        }

        if (_editorKind == "payment")
        {
            var invoice = Property(await Request("GET", "api/invoices/" + _editorId), "invoice");
            var profile = await Request("GET", "api/patients/" + Integer(invoice, "patientId"));
            result["policies"] = Items(Property(profile, "policies")).Where(p => Text(p, "kind") == "ДМС" && Boolean(p, "isActive")).Select(p => new Choice(Text(p, "id"), Text(p, "number") + " · " + Text(p, "companyName"))).ToList();
        }

        result["gender"] = [new("М", "Мужской"), new("Ж", "Женский")];
        result["boolean"] = [new("false", "Нет"), new("true", "Да")];
        result["dictionary_types"] = DictionaryRoutes.Keys.Select(t => new Choice(t, DictionaryLabel(t))).ToList();
        return result;
    }
}
