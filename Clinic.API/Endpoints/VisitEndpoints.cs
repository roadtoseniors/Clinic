namespace Clinic.API;

// Рабочее место врача: расписание, медкарта, протокол приёма, назначения, направления
public static class VisitEndpoints
{
    public static void MapVisitEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/doctor/schedule", DoctorSchedule).RequirePermission(Perm.DoctorSchedule);

        api.MapGet("/patients/{id:int}/medcard", Medcard).RequirePermission(Perm.MedcardView);
        api.MapGet("/visits/{id:int}", GetVisit).RequirePermission(Perm.MedcardView);

        api.MapPost("/visits", StartVisit).RequirePermission(Perm.VisitManage);
        api.MapPut("/visits/{id:int}", UpdateVisit).RequirePermission(Perm.VisitManage);
        api.MapPost("/visits/{id:int}/close", CloseVisit).RequirePermission(Perm.VisitManage);

        api.MapPost("/visits/{id:int}/prescriptions", AddPrescription).RequirePermission(Perm.VisitManage);
        api.MapPut("/prescriptions/{id:int}", UpdatePrescription).RequirePermission(Perm.VisitManage);
        api.MapDelete("/prescriptions/{id:int}", DeletePrescription).RequirePermission(Perm.VisitManage);

        api.MapPost("/visits/{id:int}/referrals", AddReferral).RequirePermission(Perm.ReferralIssue);
        api.MapDelete("/referrals/{id:int}", DeleteReferral).RequirePermission(Perm.ReferralIssue);
        api.MapPost("/referrals/{id:int}/cancel", CancelReferral).RequirePermission(Perm.ReferralIssue);
    }

    // GET /api/doctor/schedule?from=2026-10-06&to=2026-10-13 — дни врача с записями пациентов
    private static async Task<IResult> DoctorSchedule(DateOnly? from, DateOnly? to, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        if (user.GetEmployeeId() is not int employeeId) return Err.Forbidden("Раздел доступен только сотруднику");

        var start = from ?? Clock.Today;
        var end = to ?? start.AddDays(7);
        if (end < start) return Err.BadRequest("Дата «по» раньше даты «с»");

        var days = await db.Schedules.AsNoTracking()
            .Where(s => s.EmployeeId == employeeId && s.WorkDate >= start && s.WorkDate <= end)
            .OrderBy(s => s.WorkDate)
            .Select(s => new DoctorDayDto(
                s.Id, s.WorkDate, s.StartTime, s.EndTime, s.SlotMinutes, s.Room.Number,
                s.Appointments
                    .Where(a => a.Status != AppointmentStatus.Cancelled)
                    .OrderBy(a => a.StartTime)
                    .Select(a => new DoctorAppointmentDto(
                        a.Id, a.StartTime, a.PatientId,
                        (a.Patient.LastName + " " + a.Patient.FirstName + " " + (a.Patient.MiddleName ?? "")).Trim(),
                        a.Patient.CardNumber, a.Patient.BirthDate, a.Status,
                        a.Visit != null ? (int?)a.Visit.Id : null,
                        a.Visit != null ? (bool?)a.Visit.IsClosed : null))
                    .ToList()))
            .ToListAsync(ct);

        return Results.Ok(days);
    }

    // GET /api/patients/{id}/medcard — вся медкарта пациента (каждый просмотр пишется в журнал)
    private static async Task<IResult> Medcard(int id, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var profile = await MedcardQueries.ProfileAsync(db, id, ct);
        if (profile is null) return Err.NotFound("Пациент не найден");

        var visits = await MedcardQueries.VisitsAsync(db, id, ct);
        await audit.LogAsync(user, AuditAction.Select, "visit", null, id, ct);
        return Results.Ok(new MedcardDto(profile, visits));
    }

    private static async Task<IResult> GetVisit(int id, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var visit = await MedcardQueries.VisitAsync(db, id, ct);
        if (visit is null) return Err.NotFound("Приём не найден");

        await audit.LogAsync(user, AuditAction.Select, "visit", id, visit.PatientId, ct);
        return Results.Ok(visit);
    }

    // Загрузка своего незакрытого приёма для изменения
    private static async Task<(IResult? Error, Visit? Visit)> LoadOwnOpenVisitAsync(
        int visitId, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        var visit = await db.Visits
            .Include(v => v.Appointment).ThenInclude(a => a.Schedule)
            .FirstOrDefaultAsync(v => v.Id == visitId, ct);

        if (visit is null)
            return (Err.NotFound("Приём не найден"), null);
        if (visit.Appointment.Schedule.EmployeeId != user.GetEmployeeId())
            return (Err.Forbidden("Это приём другого врача"), null);
        if (visit.IsClosed)
            return (Err.Conflict("Приём закрыт, изменения запрещены"), null);
        return (null, visit);
    }

    private static Task<VisitDto> LoadVisitDto(MyDbContext db, int id, CancellationToken ct) =>
        db.Visits.AsNoTracking().Where(v => v.Id == id).Select(Projections.Visits).FirstAsync(ct);

    // POST /api/visits  { "appointmentId": 15 } — врач начинает приём по записи
    private static async Task<IResult> StartVisit(CreateVisitDto dto, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var appointment = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == dto.AppointmentId)
            .Select(a => new { a.Id, a.PatientId, a.Status, a.Schedule.EmployeeId, a.Schedule.WorkDate })
            .FirstOrDefaultAsync(ct);

        if (appointment is null) return Err.NotFound("Запись не найдена");
        if (appointment.EmployeeId != user.GetEmployeeId()) return Err.Forbidden("Это запись к другому врачу");
        if (appointment.Status != AppointmentStatus.Planned) return Err.Conflict($"Запись в статусе «{appointment.Status}», приём начать нельзя");
        if (appointment.WorkDate > Clock.Today) return Err.Conflict("День приёма ещё не наступил");

        var existingId = await db.Visits.Where(v => v.AppointmentId == appointment.Id).Select(v => (int?)v.Id).FirstOrDefaultAsync(ct);
        if (existingId is int existing) return Err.Conflict($"Приём по этой записи уже начат (visitId = {existing})");

        var visit = new Visit
        {
            AppointmentId = appointment.Id,
            VisitedAt = Clock.Now,
            IsClosed = false
        };
        db.Visits.Add(visit);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(user, AuditAction.Insert, "visit", visit.Id, appointment.PatientId, ct);

        return Results.Created($"/api/visits/{visit.Id}", await LoadVisitDto(db, visit.Id, ct));
    }

    // PUT /api/visits/{id} — протокол: жалобы, анамнез, осмотр, диагноз, рекомендации
    private static async Task<IResult> UpdateVisit(int id, VisitUpdateDto dto, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var (error, visit) = await LoadOwnOpenVisitAsync(id, user, db, ct);
        if (error is not null) return error;

        var diagnosisCode = Text.Clean(dto.DiagnosisCode)?.ToUpperInvariant();
        if (diagnosisCode is not null && !await db.Diagnoses.AnyAsync(d => d.Code == diagnosisCode, ct))
            return Err.BadRequest("Диагноз с таким кодом не найден");

        visit!.Complaints = Text.Clean(dto.Complaints);
        visit.Anamnesis = Text.Clean(dto.Anamnesis);
        visit.ObjectiveStatus = Text.Clean(dto.ObjectiveStatus);
        visit.DiagnosisCode = diagnosisCode;
        visit.Recommendations = Text.Clean(dto.Recommendations);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(user, AuditAction.Update, "visit", id, visit.Appointment.PatientId, ct);

        return Results.Ok(await LoadVisitDto(db, id, ct));
    }

    // POST /api/visits/{id}/close — закрыть приём (нужен диагноз); запись получает статус «завершена»
    private static async Task<IResult> CloseVisit(int id, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var (error, visit) = await LoadOwnOpenVisitAsync(id, user, db, ct);
        if (error is not null) return error;
        if (visit!.DiagnosisCode is null) return Err.BadRequest("Перед закрытием приёма укажите диагноз");

        visit.IsClosed = true;
        visit.Appointment.Status = AppointmentStatus.Completed;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(user, AuditAction.Update, "visit", id, visit.Appointment.PatientId, ct);

        return Results.Ok(await LoadVisitDto(db, id, ct));
    }

    // POST /api/visits/{id}/prescriptions — назначение или рецепт (isRecipe = true)
    private static async Task<IResult> AddPrescription(int id, PrescriptionInput dto, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var (error, visit) = await LoadOwnOpenVisitAsync(id, user, db, ct);
        if (error is not null) return error;
        if (!await db.Drugs.AnyAsync(d => d.Id == dto.DrugId, ct)) return Err.BadRequest("Препарат не найден");

        if(string.IsNullOrWhiteSpace(dto.Dosage)||string.IsNullOrWhiteSpace(dto.Instruction)||dto.Dosage.Length>100)return Err.BadRequest("Укажите дозировку до 100 символов и инструкцию");
        var prescription = new Prescription
        {
            VisitId = id,
            DrugId = dto.DrugId,
            Dosage = Text.Clean(dto.Dosage),
            Instruction = Text.Clean(dto.Instruction),
            IsRecipe = dto.IsRecipe
        };
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(user, AuditAction.Insert, "prescription", prescription.Id, visit!.Appointment.PatientId, ct);

        var result = await db.Prescriptions.AsNoTracking().Where(p => p.Id == prescription.Id).Select(Projections.Prescriptions).FirstAsync(ct);
        return Results.Created($"/api/prescriptions/{prescription.Id}", result);
    }

    private static async Task<IResult> UpdatePrescription(int id, PrescriptionInput dto, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var prescription=await db.Prescriptions.FirstOrDefaultAsync(p=>p.Id==id,ct);
        if(prescription is null)return Err.NotFound("Назначение не найдено");
        var (error,visit)=await LoadOwnOpenVisitAsync(prescription.VisitId,user,db,ct);
        if(error is not null)return error;
        if(!await db.Drugs.AnyAsync(d=>d.Id==dto.DrugId,ct))return Err.BadRequest("Препарат не найден");
        if(string.IsNullOrWhiteSpace(dto.Dosage)||string.IsNullOrWhiteSpace(dto.Instruction))return Err.BadRequest("Укажите дозировку и инструкцию");
        if(dto.Dosage.Length>100)return Err.BadRequest("Дозировка — не более 100 символов");
        prescription.DrugId=dto.DrugId;prescription.Dosage=dto.Dosage.Trim();prescription.Instruction=dto.Instruction.Trim();prescription.IsRecipe=dto.IsRecipe;
        await db.SaveChangesAsync(ct);await audit.LogAsync(user,AuditAction.Update,"prescription",id,visit!.Appointment.PatientId,ct);
        return Results.Ok(await db.Prescriptions.Where(p=>p.Id==id).Select(Projections.Prescriptions).SingleAsync(ct));
    }

    // DELETE /api/prescriptions/{id} — только пока приём не закрыт
    private static async Task<IResult> DeletePrescription(int id, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var visitId = await db.Prescriptions.Where(p => p.Id == id).Select(p => (int?)p.VisitId).FirstOrDefaultAsync(ct);
        if (visitId is null) return Err.NotFound("Назначение не найдено");

        var (error, visit) = await LoadOwnOpenVisitAsync(visitId.Value, user, db, ct);
        if (error is not null) return error;

        await db.Prescriptions.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        await audit.LogAsync(user, AuditAction.Delete, "prescription", id, visit!.Appointment.PatientId, ct);
        return Results.NoContent();
    }

    // POST /api/visits/{id}/referrals  { "serviceId": 7 } — направление на исследование
    private static async Task<IResult> AddReferral(int id, ReferralInput dto, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var (error, visit) = await LoadOwnOpenVisitAsync(id, user, db, ct);
        if (error is not null) return error;
        if (!await db.Services.AnyAsync(s => s.Id == dto.ServiceId && s.IsActive, ct)) return Err.BadRequest("Услуга не найдена");

        var referral = new Referral
        {
            VisitId = id,
            ServiceId = dto.ServiceId,
            Status = ReferralStatus.Issued
        };
        db.Referrals.Add(referral);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(user, AuditAction.Insert, "referral", referral.Id, visit!.Appointment.PatientId, ct);

        var result = await db.Referrals.AsNoTracking().Where(r => r.Id == referral.Id).Select(Projections.Referrals).FirstAsync(ct);
        return Results.Created($"/api/referrals/{referral.Id}", result);
    }

    // POST /api/referrals/{id}/cancel — отменить ещё не выполненное направление (только врач, который его выдал)
    private static async Task<IResult> CancelReferral(int id, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var referral = await db.Referrals
            .Include(r => r.Visit).ThenInclude(v => v.Appointment).ThenInclude(a => a.Schedule)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (referral is null) return Err.NotFound("Направление не найдено");
        if (referral.Visit.Appointment.Schedule.EmployeeId != user.GetEmployeeId())
            return Err.Forbidden("Направление выдано другим врачом");
        if (referral.Status != ReferralStatus.Issued)
            return Err.Conflict($"Направление в статусе «{referral.Status}», отменить нельзя");

        referral.Status = ReferralStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(user, AuditAction.Update, "referral", id, referral.Visit.Appointment.PatientId, ct);
        return Results.NoContent();
    }
    private static async Task<IResult> DeleteReferral(int id, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var referral=await db.Referrals.FirstOrDefaultAsync(r=>r.Id==id,ct);
        if(referral is null)return Err.NotFound("Направление не найдено");
        var (error,visit)=await LoadOwnOpenVisitAsync(referral.VisitId,user,db,ct);
        if(error is not null)return error;
        if(referral.Status==ReferralStatus.Done)return Err.Conflict("Выполненное исследование удалять нельзя");
        await db.Referrals.Where(r=>r.Id==id).ExecuteDeleteAsync(ct);
        await audit.LogAsync(user,AuditAction.Delete,"referral",id,visit!.Appointment.PatientId,ct);
        return Results.NoContent();
    }

}
