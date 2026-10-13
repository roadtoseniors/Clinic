using System.Security.Claims;
using Clinic.Context;
using Microsoft.EntityFrameworkCore;

namespace Clinic.API;

// Запись на приём: создание, перенос, отмена, неявка.
// Пациент работает только со своими записями (id из токена), регистратор — с любыми.
// Уведомления пациенту о записи, переносе и отмене создают триггеры базы.
public static class AppointmentEndpoints
{
    public static void MapAppointmentEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/appointments", List).RequirePermission(Perm.AppointmentManage);
        api.MapGet("/appointments/{id:int}", Get).RequirePermission(Perm.AppointmentManage, Perm.OwnView, Perm.DoctorSchedule);
        api.MapPost("/appointments", Create).RequirePermission(Perm.OwnBook, Perm.AppointmentManage);
        api.MapPost("/appointments/{id:int}/reschedule", Reschedule).RequirePermission(Perm.OwnBook, Perm.AppointmentManage);
        api.MapPost("/appointments/{id:int}/cancel", Cancel).RequirePermission(Perm.OwnBook, Perm.AppointmentManage);
        api.MapPost("/appointments/{id:int}/no-show", NoShow).RequirePermission(Perm.AppointmentManage, Perm.VisitManage);
    }

    // Пациент без права appointment.manage действует только от своего имени
    private static bool ActsForSelf(ClaimsPrincipal user) => !user.HasPermission(Perm.AppointmentManage);

    // GET /api/appointments?date=...&from=...&to=...&employeeId=...&patientId=...&status=...&page=1
    private static async Task<IResult> List(DateOnly? date, DateOnly? from, DateOnly? to, int? employeeId, int? patientId,
        string? status, int? page, int? pageSize, MyDbContext db, CancellationToken ct)
    {
        var query = db.Appointments.AsNoTracking();
        if (date is DateOnly d) query = query.Where(a => a.Schedule.WorkDate == d);
        if (from is DateOnly f) query = query.Where(a => a.Schedule.WorkDate >= f);
        if (to is DateOnly t) query = query.Where(a => a.Schedule.WorkDate <= t);
        if (employeeId is int e) query = query.Where(a => a.Schedule.EmployeeId == e);
        if (patientId is int p) query = query.Where(a => a.PatientId == p);
        if (Text.Clean(status) is string s) query = query.Where(a => a.Status == s);

        return Results.Ok(await query
            .OrderByDescending(a => a.Schedule.WorkDate).ThenBy(a => a.StartTime).ThenBy(a => a.Id)
            .Select(Projections.Appointments)
            .ToPagedAsync(page, pageSize, ct));
    }

    private static async Task<IResult> Get(int id, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        var appointment = await db.Appointments.AsNoTracking().Where(a => a.Id == id).Select(Projections.Appointments).FirstOrDefaultAsync(ct);
        if (appointment is null) return Err.NotFound("Запись не найдена");

        if (!user.HasPermission(Perm.AppointmentManage))
        {
            var own = appointment.PatientId == user.GetPatientId() || appointment.EmployeeId == user.GetEmployeeId();
            if (!own) return Err.Forbidden("Это не ваша запись");
        }
        return Results.Ok(appointment);
    }

    // POST /api/appointments  { "scheduleId": 9, "startTime": "10:30", "patientId": 3 }
    private static async Task<IResult> Create(CreateAppointmentDto dto, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        int patientId;
        string source;

        if (ActsForSelf(user))
        {
            if (user.GetPatientId() is not int own) return Err.Forbidden("Записаться на приём может только пациент");
            patientId = own;
            source = AppointmentSource.App;
        }
        else
        {
            if (dto.PatientId is not int chosen) return Err.BadRequest("Укажите пациента");
            patientId = chosen;
            source = Text.Clean(dto.Source)?.ToLowerInvariant() ?? AppointmentSource.Registry;
            if (!AppointmentSource.All.Contains(source))
                return Err.BadRequest("Источник записи: регистратура, приложение, сайт или телефон");
        }

        if (!await db.Patients.AnyAsync(p => p.Id == patientId && p.IsActive, ct))
            return Err.BadRequest("Пациент не найден или неактивен");

        if (await SlotRules.CheckAsync(db, dto.ScheduleId, dto.StartTime, null, ct) is IResult slotError)
            return slotError;

        if (await db.Appointments.AnyAsync(a => a.PatientId == patientId && a.ScheduleId == dto.ScheduleId
                                             && a.Status != AppointmentStatus.Cancelled, ct))
            return Err.Conflict("Пациент уже записан к этому врачу на этот день");

        var appointment = new Appointment
        {
            ScheduleId = dto.ScheduleId,
            PatientId = patientId,
            StartTime = dto.StartTime,
            Status = AppointmentStatus.Planned,
            Source = source,
            CreatedAt = Clock.Now
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync(ct);   // если кто-то успел занять слот, индекс базы даст 409

        var result = await db.Appointments.AsNoTracking().Where(a => a.Id == appointment.Id).Select(Projections.Appointments).FirstAsync(ct);
        return Results.Created($"/api/appointments/{appointment.Id}", result);
    }

    // Загрузка записи для изменения с проверкой владельца и статуса
    private static async Task<(IResult? Error, Appointment? Appointment)> LoadForChangeAsync(
        int id, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        var appointment = await db.Appointments.Include(a => a.Schedule).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null)
            return (Err.NotFound("Запись не найдена"), null);
        if (ActsForSelf(user) && appointment.PatientId != user.GetPatientId())
            return (Err.Forbidden("Это не ваша запись"), null);
        if (appointment.Status != AppointmentStatus.Planned)
            return (Err.Conflict($"Запись в статусе «{appointment.Status}» изменить нельзя"), null);
        return (null, appointment);
    }

    // POST /api/appointments/{id}/reschedule  { "scheduleId": 10, "startTime": "11:00" }
    private static async Task<IResult> Reschedule(int id, RescheduleDto dto, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        var (error, appointment) = await LoadForChangeAsync(id, user, db, ct);
        if (error is not null) return error;

        if (ActsForSelf(user) && appointment!.Schedule.WorkDate.ToDateTime(appointment.StartTime) < Clock.Now)
            return Err.Conflict("Время записи уже прошло, перенести её нельзя");

        if (await SlotRules.CheckAsync(db, dto.ScheduleId, dto.StartTime, id, ct) is IResult slotError)
            return slotError;

        if (dto.ScheduleId != appointment!.ScheduleId
            && await db.Appointments.AnyAsync(a => a.PatientId == appointment.PatientId && a.ScheduleId == dto.ScheduleId
                                                && a.Status != AppointmentStatus.Cancelled && a.Id != id, ct))
            return Err.Conflict("Пациент уже записан к этому врачу на этот день");

        appointment.ScheduleId = dto.ScheduleId;
        appointment.StartTime = dto.StartTime;
        await db.SaveChangesAsync(ct);

        var result = await db.Appointments.AsNoTracking().Where(a => a.Id == id).Select(Projections.Appointments).FirstAsync(ct);
        return Results.Ok(result);
    }

    // POST /api/appointments/{id}/cancel — статус «отменена», слот освобождается
    private static async Task<IResult> Cancel(int id, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        var (error, appointment) = await LoadForChangeAsync(id, user, db, ct);
        if (error is not null) return error;

        if (ActsForSelf(user) && appointment!.Schedule.WorkDate.ToDateTime(appointment.StartTime) < Clock.Now)
            return Err.Conflict("Время записи уже прошло, отменить её нельзя");
        if (await db.Visits.AnyAsync(v => v.AppointmentId == id, ct))
            return Err.Conflict("Приём по этой записи уже начат");

        appointment!.Status = AppointmentStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // POST /api/appointments/{id}/no-show — пациент не пришёл
    private static async Task<IResult> NoShow(int id, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        var appointment = await db.Appointments.Include(a => a.Schedule).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null) return Err.NotFound("Запись не найдена");

        // Врач без права appointment.manage отмечает неявку только на своих приёмах
        if (!user.HasPermission(Perm.AppointmentManage) && appointment.Schedule.EmployeeId != user.GetEmployeeId())
            return Err.Forbidden("Это запись к другому врачу");
        if (appointment.Status != AppointmentStatus.Planned)
            return Err.Conflict($"Запись в статусе «{appointment.Status}»");
        if (appointment.Schedule.WorkDate.ToDateTime(appointment.StartTime) > Clock.Now)
            return Err.Conflict("Время приёма ещё не наступило");
        if (await db.Visits.AnyAsync(v => v.AppointmentId == id, ct))
            return Err.Conflict("Приём по этой записи уже начат");

        appointment.Status = AppointmentStatus.NoShow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
