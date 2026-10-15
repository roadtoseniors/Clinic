namespace Clinic.API;

// Значения строк ровно как в CHECK-ограничениях clinic.sql.
// Русские строки пишем только здесь, чтобы не ошибиться буквой в разных местах кода.

public static class AppointmentStatus
{
    public const string Planned = "запланирована";
    public const string Completed = "завершена";
    public const string NoShow = "неявка";
    public const string Cancelled = "отменена";
}

public static class AppointmentSource
{
    public const string Registry = "регистратура";
    public const string App = "приложение";
    public const string Site = "сайт";
    public const string Phone = "телефон";

    public static readonly string[] All = { Registry, App, Site, Phone };
}

public static class InvoiceStatus
{
    public const string Paid = "оплачен";
    public const string Unpaid = "не оплачен";
    public const string Cancelled = "отменён";
}

public static class ReferralStatus
{
    public const string Issued = "выдано";
    public const string Done = "выполнено";
    public const string Cancelled = "отменено";
}

// ⚠ Сверь с CHECK у patient_policy.kind
public static class PolicyKind
{
    public const string Oms = "ОМС";
    public const string Dms = "ДМС";

    public static readonly string[] All = { Oms, Dms };
}

// ⚠ Сверь с CHECK у audit.action
public static class AuditAction
{
    public const string Select = "SELECT";
    public const string Insert = "INSERT";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
}

// ⚠ Сверь с CHECK у backup_log
public static class BackupAction
{
    public const string Backup = "backup";
    public const string Restore = "restore";
}

// Коды ролей из колонки role.code
public static class RoleCode
{
    public const string Admin = "admin";
    public const string Doctor = "doctor";
    public const string Registrar = "registrar";
    public const string Lab = "lab";
    public const string Patient = "patient";
}

public static class Theme
{
    public const string Light = "light";
    public const string Dark = "dark";
}
