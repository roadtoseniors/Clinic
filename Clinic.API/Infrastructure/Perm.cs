namespace Clinic.API;

// Коды прав из таблицы permission. Константы вместо строк: опечатка не скомпилируется.
public static class Perm
{
    public const string ClaimType = "permission";

    public const string OwnView = "own.view";
    public const string OwnBook = "own.book";
    public const string PatientView = "patient.view";
    public const string PatientManage = "patient.manage";
    public const string DocumentManage = "document.manage";
    public const string ScheduleManage = "schedule.manage";
    public const string AppointmentManage = "appointment.manage";
    public const string ServiceRender = "service.render";
    public const string InvoiceManage = "invoice.manage";
    public const string DoctorSchedule = "doctor.schedule";
    public const string MedcardView = "medcard.view";
    public const string VisitManage = "visit.manage";
    public const string ReferralIssue = "referral.issue";
    public const string ReferralView = "referral.view";
    public const string ReferralResult = "referral.result";
    public const string AccountManage = "account.manage";
    public const string RoleManage = "role.manage";
    public const string ReferenceManage = "reference.manage";
    public const string ReportView = "report.view";
    public const string AuditView = "audit.view";
    public const string BackupManage = "backup.manage";
}

public static class AuthExtensions
{
    // Доступ, если у пользователя есть ЛЮБОЕ из перечисленных прав.
    // Работает и для одного эндпоинта, и для целой группы (MapGroup).
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder
        => builder.RequireAuthorization(p => p.RequireClaim(Perm.ClaimType, permissions));

    // Только сотрудники (в токене есть employeeId). Пациенты сюда не попадут.
    public static TBuilder RequireStaff<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.RequireAuthorization(p => p.RequireClaim("employeeId"));

    public static bool HasPermission(this ClaimsPrincipal user, string permission) =>
        user.HasClaim(Perm.ClaimType, permission);

    public static int? GetAccountId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    public static int? GetPatientId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst("patientId")?.Value, out var id) ? id : null;

    public static int? GetEmployeeId(this ClaimsPrincipal user) =>
        int.TryParse(user.FindFirst("employeeId")?.Value, out var id) ? id : null;
}
