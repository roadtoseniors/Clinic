namespace Clinic.API;

// Отчёты администратора. Период по умолчанию — последние 30 дней.
// Считаем через LINQ по основным таблицам; представления v_* в базе остаются для pgAdmin и отчётов в SQL.
public static class ReportEndpoints
{
    public static void MapReportEndpoints(this RouteGroupBuilder api)
    {
        var reports = api.MapGroup("/reports").RequirePermission(Perm.ReportView);
        reports.MapGet("/attendance", Attendance);
        reports.MapGet("/load", DoctorLoad);
        reports.MapGet("/morbidity", Morbidity);
        reports.MapGet("/revenue", Revenue);
    }

    // GET /api/reports/attendance?from=&to= — посещаемость по дням
    private static async Task<IResult> Attendance(DateOnly? from, DateOnly? to, MyDbContext db, CancellationToken ct)
    {
        if (Rules.ValidateRange(from, to, 366, out var start, out var end) is string error) return Err.BadRequest(error);

        var rows = await db.Appointments.AsNoTracking()
            .Where(a => a.Schedule.WorkDate >= start && a.Schedule.WorkDate <= end)
            .GroupBy(a => a.Schedule.WorkDate)
            .OrderBy(g => g.Key)
            .Select(g => new AttendanceRow(
                g.Key,
                g.Count(),
                g.Count(a => a.Status == AppointmentStatus.Planned),
                g.Count(a => a.Status == AppointmentStatus.Completed),
                g.Count(a => a.Status == AppointmentStatus.NoShow),
                g.Count(a => a.Status == AppointmentStatus.Cancelled)))
            .ToListAsync(ct);

        return Results.Ok(rows);
    }

    // GET /api/reports/load?from=&to= — загрузка врачей: занятые слоты / все слоты
    private static async Task<IResult> DoctorLoad(DateOnly? from, DateOnly? to, MyDbContext db, CancellationToken ct)
    {
        if (Rules.ValidateRange(from, to, 366, out var start, out var end) is string error) return Err.BadRequest(error);

        var schedules = await db.Schedules.AsNoTracking()
            .Where(s => s.WorkDate >= start && s.WorkDate <= end)
            .Select(s => new
            {
                s.EmployeeId,
                DoctorName = s.Employee.LastName + " " + s.Employee.FirstName,
                s.StartTime,
                s.EndTime,
                s.SlotMinutes,
                Booked = s.Appointments.Count(a => a.Status != AppointmentStatus.Cancelled)
            })
            .ToListAsync(ct);

        var rows = schedules
            .GroupBy(s => new { s.EmployeeId, s.DoctorName })
            .Select(g =>
            {
                var total = g.Sum(s => SlotRules.All(s.StartTime, s.EndTime, s.SlotMinutes).Count());
                var booked = g.Sum(s => s.Booked);
                var percent = total == 0 ? 0 : Math.Round(booked * 100m / total, 1);
                return new DoctorLoadRow(g.Key.EmployeeId, g.Key.DoctorName, g.Count(), total, booked, percent);
            })
            .OrderByDescending(r => r.LoadPercent)
            .ToList();

        return Results.Ok(rows);
    }

    // GET /api/reports/morbidity?from=&to= — заболеваемость по диагнозам закрытых приёмов
    private static async Task<IResult> Morbidity(DateOnly? from, DateOnly? to, MyDbContext db, CancellationToken ct)
    {
        if (Rules.ValidateRange(from, to, 366, out var start, out var end) is string error) return Err.BadRequest(error);
        var fromDt = start.ToDateTime(TimeOnly.MinValue);
        var toDt = end.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var visits = await db.Visits.AsNoTracking()
            .Where(v => v.VisitedAt >= fromDt && v.VisitedAt < toDt && v.IsClosed && v.DiagnosisCode != null)
            .Select(v => new { Code = v.DiagnosisCode!, v.Appointment.PatientId })
            .ToListAsync(ct);

        var codes = visits.Select(v => v.Code).Distinct().ToList();
        var names = await db.Diagnoses.AsNoTracking()
            .Where(d => codes.Contains(d.Code))
            .ToDictionaryAsync(d => d.Code, d => d.Name, ct);

        var rows = visits
            .GroupBy(v => v.Code)
            .Select(g => new MorbidityRow(g.Key, names.GetValueOrDefault(g.Key, ""), g.Count(), g.Select(v => v.PatientId).Distinct().Count()))
            .OrderByDescending(r => r.Visits)
            .ToList();

        return Results.Ok(rows);
    }

    // GET /api/reports/revenue?from=&to= — выручка по оплаченным счетам по дням
    private static async Task<IResult> Revenue(DateOnly? from, DateOnly? to, MyDbContext db, CancellationToken ct)
    {
        if (Rules.ValidateRange(from, to, 366, out var start, out var end) is string error) return Err.BadRequest(error);
        var fromDt = start.ToDateTime(TimeOnly.MinValue);
        var toDt = end.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.Status == InvoiceStatus.Paid && i.PaidAt >= fromDt && i.PaidAt < toDt)
            .Select(i => new { PaidAt = i.PaidAt!.Value, Total = i.VisitServices.Sum(vs => vs.Price * vs.Quantity) })
            .ToListAsync(ct);

        var rows = invoices
            .GroupBy(i => DateOnly.FromDateTime(i.PaidAt))
            .OrderBy(g => g.Key)
            .Select(g => new RevenueRow(g.Key, g.Count(), g.Sum(i => i.Total)))
            .ToList();

        return Results.Ok(rows);
    }
}
