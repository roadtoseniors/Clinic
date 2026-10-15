namespace Clinic.API;

// Запросы медкарты: используются и врачом (/api/patients/{id}/medcard), и пациентом (/api/my/medcard)
public static class MedcardQueries
{
    public static async Task<PatientProfileDto?> ProfileAsync(MyDbContext db, int patientId, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking()
            .Where(p => p.Id == patientId)
            .Select(Projections.Patients)
            .FirstOrDefaultAsync(ct);

        if (patient is null) return null;

        var policies = await db.PatientPolicies.AsNoTracking()
            .Where(p => p.PatientId == patientId)
            .OrderByDescending(p => p.IsActive).ThenBy(p => p.Kind).ThenByDescending(p => p.ValidFrom)
            .Select(Projections.Policies)
            .ToListAsync(ct);

        var attachment = await db.PatientAttachments.AsNoTracking()
            .Where(a => a.PatientId == patientId && a.AttachedTo == null)
            .Select(Projections.Attachments)
            .FirstOrDefaultAsync(ct);

        return new PatientProfileDto(patient, policies, attachment);
    }

    public static async Task<List<VisitDetailsDto>> VisitsAsync(MyDbContext db, int patientId, CancellationToken ct)
    {
        var visits = await db.Visits.AsNoTracking()
            .Where(v => v.Appointment.PatientId == patientId)
            .OrderByDescending(v => v.VisitedAt)
            .Select(Projections.VisitDetails)
            .ToListAsync(ct);

        return await WithDiagnosisNamesAsync(db, visits, ct);
    }

    public static async Task<VisitDetailsDto?> VisitAsync(MyDbContext db, int visitId, CancellationToken ct)
    {
        var visit = await db.Visits.AsNoTracking()
            .Where(v => v.Id == visitId)
            .Select(Projections.VisitDetails)
            .FirstOrDefaultAsync(ct);

        if (visit is null) return null;
        return (await WithDiagnosisNamesAsync(db, [visit], ct))[0];
    }

    // Названия диагнозов берём одним запросом по всем кодам
    private static async Task<List<VisitDetailsDto>> WithDiagnosisNamesAsync(MyDbContext db, List<VisitDetailsDto> visits, CancellationToken ct)
    {
        var codes = visits.Where(v => v.DiagnosisCode != null).Select(v => v.DiagnosisCode!).Distinct().ToList();
        if (codes.Count == 0) return visits;

        var names = await db.Diagnoses.AsNoTracking()
            .Where(d => codes.Contains(d.Code))
            .ToDictionaryAsync(d => d.Code, d => d.Name, ct);

        return visits
            .Select(v => v.DiagnosisCode != null && names.TryGetValue(v.DiagnosisCode, out var name)
                ? v with { DiagnosisName = name }
                : v)
            .ToList();
    }
}
