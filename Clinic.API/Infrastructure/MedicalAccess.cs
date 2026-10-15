namespace Clinic.API;

public static class MedicalAccess
{
    public static IQueryable<Patient> Patients(MyDbContext db, ClaimsPrincipal user)
    {
        var query = db.Patients.AsQueryable();
        if (!user.IsInRole(RoleCode.Doctor)) return query;
        var employee = user.GetEmployeeId();
        return query.Where(p => p.Appointments.Any(a => a.Schedule.EmployeeId == employee && a.Status != AppointmentStatus.Cancelled)
            || db.PatientAttachments.Any(a => a.PatientId == p.Id && a.EmployeeId == employee && a.AttachedTo == null));
    }
    public static Task<bool> CanReadAsync(MyDbContext db, ClaimsPrincipal user, int patient, CancellationToken ct) =>
        Patients(db, user).AnyAsync(p => p.Id == patient, ct);
}

public sealed class MedicalAccessFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.User;
        if (!user.IsInRole(RoleCode.Doctor)) return await next(context);
        var parts = context.HttpContext.Request.Path.Value!.Trim('/').Split('/');
        if (parts.Length < 3 || !int.TryParse(parts[2], out var id)) return await next(context);
        var db = context.HttpContext.RequestServices.GetRequiredService<MyDbContext>();
        var ct = context.HttpContext.RequestAborted;
        int? patient = parts[1] switch
        {
            "patients" => id,
            "visits" => await db.Visits.Where(v => v.Id == id).Select(v => (int?)v.Appointment.PatientId).FirstOrDefaultAsync(ct),
            "referrals" => await db.Referrals.Where(v => v.Id == id).Select(v => (int?)v.Visit.Appointment.PatientId).FirstOrDefaultAsync(ct),
            "referral-files" => await db.ReferralFiles.Where(v => v.Id == id).Select(v => (int?)v.Referral.Visit.Appointment.PatientId).FirstOrDefaultAsync(ct),
            "documents" => await db.Documents.Where(v => v.Id == id).Select(v => (int?)v.PatientId).FirstOrDefaultAsync(ct),
            _ => null
        };
        if (patient.HasValue && !await MedicalAccess.CanReadAsync(db, user, patient.Value, ct))
            return Err.Forbidden("Нет доступа к карте этого пациента");
        if (parts[1] == "schedules" && await db.Schedules.AnyAsync(s => s.Id == id && s.EmployeeId != user.GetEmployeeId(), ct))
            return Err.Forbidden("Это расписание другого врача");
        return await next(context);
    }
}
