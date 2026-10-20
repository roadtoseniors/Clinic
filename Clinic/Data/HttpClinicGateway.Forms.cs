using System;
using System.Collections.Generic;
using System.Linq;
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

    public FieldSpec[] FieldsFor(string kind, int id = 0)
    {
        switch (kind)
        {
            case "role_permissions":
                return _permissionFields;
            case "booking" when _session?.Role == "Пациент":
                return Catalog.Fields(kind).Where(field => field.Key != "patient_id").ToArray();
            case "rendered" when id > 0:
                return Catalog.Fields(kind).Where(field => field.Key != "visit_id").ToArray();
            case "patient":
                var patientFields = new[]
                {
                    "last_name", "first_name", "middle_name", "birth_date", "gender",
                    "oms_policy", "snils", "dms_policy", "phone", "address", "primary_doctor_id"
                };
                return Catalog.Fields("patient")
                    .Where(field => patientFields.Contains(field.Key))
                    .Concat(new FieldSpec[]
                    {
                        new("oms_company_id", "Страховая ОМС", true, "companies"),
                        new("dms_company_id", "Страховая ДМС", Lookup: "companies"),
                        new("district_id", "Участок", Lookup: "districts")
                    })
                    .ToArray();
            case "prescription":
                return Catalog.Fields(kind)
                    .Where(field => field.Key != "valid_until" && (id == 0 || field.Key != "visit_id"))
                    .ToArray();
            case "referral":
                return Catalog.Fields(kind).Where(field => field.Key != "consultation_doctor_id").ToArray();
            case "payment":
                return
                [
                    new("method_id", "Способ оплаты (полная сумма счёта)", true, "methods"),
                    new("policy_id", "Полис, только при оплате по ДМС", Lookup: "policies")
                ];
            case "document":
                return
                [
                    new("patient_id", "Пациент", true, "patients"),
                    new("type_id", "Вид документа", true, "document_types"),
                    new("visit_id", "Приём (обязательно для справки врача)", Lookup: "visits"),
                    new("content", "Текст документа", true, Multiline: true)
                ];
            case "account" when id == 0:
                return Catalog.Fields(kind)
                    .Select(field => field.Key == "password"
                        ? field with { Label = "Пароль (от 8 символов)", Required = true }
                        : field)
                    .ToArray();
            case "account" when id > 0:
                return
                [
                    new("role_id", "Роль", true, "roles"),
                    new("password", "Новый пароль (пусто — оставить текущий)", Secret: true)
                ];
            case "registration":
                return
                [
                    new("last_name", "Фамилия", true),
                    new("first_name", "Имя", true),
                    new("middle_name", "Отчество"),
                    new("birth_date", "Дата рождения (ГГГГ-ММ-ДД)", true, Default: null),
                    new("gender", "Пол", true, "gender"),
                    new("snils", "СНИЛС"),
                    new("phone", "Телефон"),
                    new("address", "Адрес"),
                    new("oms_policy", "Полис ОМС", true),
                    new("oms_company_id", "Страховая ОМС", true, "companies"),
                    new("login", "Логин", true),
                    new("password", "Пароль (от 8 символов)", true, Secret: true),
                    new("password_confirmation", "Подтверждение пароля", true, Secret: true)
                ];
            default:
                return Catalog.Fields(kind);
        }
    }

    public async Task<Dictionary<string, List<Choice>>> RegistrationChoicesAsync()
    {
        var companies = await Many("auth/insurancecompanies");
        return new Dictionary<string, List<Choice>>
        {
            ["gender"] = [new("М", "Мужской"), new("Ж", "Женский")],
            ["companies"] = companies
                .Select(company => new Choice(Text(company, "id"), Text(company, "name")))
                .ToList()
        };
    }

    public async Task<Dictionary<string, List<Choice>>> ChoicesAsync(ClinicSession session)
    {
        Check(session);
        var result = new Dictionary<string, List<Choice>>();
        var needed = FieldsFor(_editorKind, _editorId)
            .Where(field => field.Lookup is not null)
            .Select(field => field.Lookup)
            .ToHashSet();
        bool Needs(string key) => _editorKind.Length == 0 || needed.Contains(key);

        if (_editorKind == "booking")
        {
            needed.Add("doctors");
        }

        var dictionaries = new[]
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
        };
        foreach (var (key, route) in dictionaries)
        {
            if (!Needs(key))
            {
                continue;
            }

            var choices = new List<Choice>();
            foreach (var item in await Many("api/" + route))
            {
                var choiceId = route == "diagnoses" ? Text(item, "code") : Text(item, "id");
                var label = route switch
                {
                    "rooms" => Text(item, "number"),
                    "doctors" => Text(item, "fullName"),
                    "diagnoses" => Text(item, "code") + " · " + Text(item, "name"),
                    _ => Text(item, "name")
                };
                choices.Add(new Choice(choiceId, label));
            }

            result[key] = choices;
        }

        if (session.Role == "Администратор" &&
            (Needs("roles") || Needs("employees") || _editorKind == "role_permissions"))
        {
            var roles = await Many("api/roles");
            var supportedRoles = new[] { "patient", "registrar", "doctor", "admin", "administrator" };
            result["roles"] = roles
                .Where(role => supportedRoles.Contains(Text(role, "code")))
                .Select(role => new Choice(Text(role, "id"), Text(role, "name")))
                .ToList();
            if (_editorKind == "role_permissions")
            {
                var role = (await Many("api/roles")).First(item => Integer(item, "id") == _editorId);
                var enabled = Items(Property(role, "permissions"))
                    .Select(permission => permission.ToString())
                    .ToHashSet();
                var fields = new List<FieldSpec>();
                foreach (var permission in await Many("api/permissions"))
                {
                    var code = Text(permission, "code");
                    fields.Add(new FieldSpec("perm:" + code, Catalog.PermissionName(code), true, "boolean",
                        Default: enabled.Contains(code) ? "true" : "false"));
                }

                _permissionFields = fields.ToArray();
            }

            var employees = await Many("api/employees");
            result["employees"] = employees
                .Select(employee => new Choice(Text(employee, "id"), FullName(employee)))
                .ToList();
        }

        if (Needs("patients") && (session.Role != "Администратор" || _editorKind == "account"))
        {
            var patients = await Patients(session);
            result["patients"] = patients
                .Where(patient => Boolean(patient, "isActive"))
                .Select(patient => new Choice(Text(patient, "id"), FullName(patient)))
                .ToList();
        }

        if (session.Role is "Врач" or "Регистратор" && (Needs("visits") || Needs("open_visits")))
        {
            var visits = await Visits(session);
            result["visits"] = new List<Choice>();
            foreach (var visit in visits)
            {
                var patientName = Text(visit, "patientName");
                if (patientName.Length == 0)
                {
                    patientName = "Пациент №" + Text(visit, "patientId");
                }

                var label = patientName + " · " + DisplayDate(Text(visit, "visitedAt"));
                result["visits"].Add(new Choice(Text(visit, "id"), label));
            }

            result["open_visits"] = result["visits"]
                .Where(choice => !Boolean(visits.First(visit => Integer(visit, "id").ToString() == choice.Id), "isClosed"))
                .ToList();
        }

        if (session.Role is "Пациент" or "Регистратор" && Needs("slots"))
        {
            BookingSlots = await ListAsync(session, "slots");
            result["slots"] = BookingSlots
                .Select(slot => new Choice(slot.Values["slot"], slot.Cell1 + " · " + slot.Cell2))
                .ToList();
        }

        if (_editorKind == "payment")
        {
            var response = await Request("GET", "api/invoices/" + _editorId);
            var invoice = Property(response, "invoice");
            var profile = await Request("GET", "api/patients/" + Integer(invoice, "patientId"));
            result["policies"] = Items(Property(profile, "policies"))
                .Where(policy => Text(policy, "kind") == "ДМС" && Boolean(policy, "isActive"))
                .Select(policy => new Choice(Text(policy, "id"), Text(policy, "number") + " · " + Text(policy, "companyName")))
                .ToList();
        }

        result["gender"] = [new("М", "Мужской"), new("Ж", "Женский")];
        result["boolean"] = [new("false", "Нет"), new("true", "Да")];
        result["dictionary_types"] = DictionaryRoutes.Keys.Select(type => new Choice(type, DictionaryLabel(type))).ToList();
        return result;
    }
}
