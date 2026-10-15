namespace Clinic.API;

// Расписание врачей и свободные слоты для записи
public static class ScheduleEndpoints
{
    public static void MapScheduleEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/schedules", List).RequireStaff();
        api.MapGet("/schedules/{id:int}", Get).RequireStaff();
        api.MapPost("/schedules", Create).RequirePermission(Perm.ScheduleManage);
        api.MapPut("/schedules/{id:int}", Update).RequirePermission(Perm.ScheduleManage);
        api.MapDelete("/schedules/{id:int}", Delete).RequirePermission(Perm.ScheduleManage);

        // Свободное время для записи: пациенту и регистратору
        api.MapGet("/slots", Slots);
    }

    // GET /api/schedules?from=2026-10-01&to=2026-10-07&employeeId=2
    private static async Task<IResult> List(DateOnly? from, DateOnly? to, int? employeeId, MyDbContext db, CancellationToken ct)
    {
        var start = from ?? Clock.Today;
        var end = to ?? start.AddDays(14);
        if (end < start) return Err.BadRequest("Дата «по» раньше даты «с»");

        var query = db.Schedules.AsNoTracking().Where(s => s.WorkDate >= start && s.WorkDate <= end);
        if (employeeId is int e) query = query.Where(s => s.EmployeeId == e);

        return Results.Ok(await query
            .OrderBy(s => s.WorkDate).ThenBy(s => s.StartTime).ThenBy(s => s.Employee.LastName)
            .Select(Projections.Schedules)
            .ToListAsync(ct));
    }

    private static async Task<IResult> Get(int id, MyDbContext db, CancellationToken ct)
    {
        var schedule = await db.Schedules.AsNoTracking().Where(s => s.Id == id).Select(Projections.Schedules).FirstOrDefaultAsync(ct);
        return schedule is null ? Err.NotFound("Расписание не найдено") : Results.Ok(schedule);
    }

    private static async Task<string?> ValidateAsync(ScheduleInput dto, int? exceptId, MyDbContext db, CancellationToken ct)
    {
        if (dto.StartTime >= dto.EndTime) return "Начало приёма должно быть раньше окончания";
        if (dto.SlotMinutes < 5 || dto.SlotMinutes > 240) return "Длина слота: от 5 до 240 минут";
        if (!SlotRules.All(dto.StartTime, dto.EndTime, dto.SlotMinutes).Any()) return "В интервал не помещается ни одного слота";
        if (dto.WorkDate < Clock.Today) return "Нельзя создавать расписание на прошедшую дату";

        if (!await db.Employees.AnyAsync(e => e.Id == dto.EmployeeId && e.IsActive, ct)) return "Сотрудник не найден или неактивен";
        if (!await db.Rooms.AnyAsync(r => r.Id == dto.RoomId, ct)) return "Кабинет не найден";

        if (await db.Schedules.AnyAsync(s => s.EmployeeId == dto.EmployeeId && s.WorkDate == dto.WorkDate && s.Id != exceptId, ct))
            return "У сотрудника уже есть расписание на этот день";

        // Кабинет не может быть занят двумя врачами одновременно
        var roomBusy = await db.Schedules.AnyAsync(s =>
            s.RoomId == dto.RoomId && s.WorkDate == dto.WorkDate && s.Id != exceptId
            && s.StartTime < dto.EndTime && dto.StartTime < s.EndTime, ct);
        if (roomBusy) return "Кабинет в это время занят другим врачом";

        return null;
    }

    private static async Task<IResult> Create(ScheduleInput dto, MyDbContext db, CancellationToken ct)
    {
        if (await ValidateAsync(dto, null, db, ct) is string error) return Err.BadRequest(error);

        var schedule = new Schedule
        {
            EmployeeId = dto.EmployeeId,
            RoomId = dto.RoomId,
            WorkDate = dto.WorkDate,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            SlotMinutes = dto.SlotMinutes
        };
        db.Schedules.Add(schedule);
        await db.SaveChangesAsync(ct);

        var result = await db.Schedules.AsNoTracking().Where(s => s.Id == schedule.Id).Select(Projections.Schedules).FirstAsync(ct);
        return Results.Created($"/api/schedules/{schedule.Id}", result);
    }

    // Если на день уже есть записи, менять можно только кабинет:
    // иначе записи пациентов выпадут из сетки слотов
    private static async Task<IResult> Update(int id, ScheduleInput dto, MyDbContext db, CancellationToken ct)
    {
        var schedule = await db.Schedules.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (schedule is null) return Err.NotFound("Расписание не найдено");
        if (await ValidateAsync(dto, id, db, ct) is string error) return Err.BadRequest(error);

        var gridChanged = schedule.EmployeeId != dto.EmployeeId || schedule.WorkDate != dto.WorkDate
            || schedule.StartTime != dto.StartTime || schedule.EndTime != dto.EndTime
            || schedule.SlotMinutes != dto.SlotMinutes;

        if (gridChanged && await db.Appointments.AnyAsync(a => a.ScheduleId == id && a.Status != AppointmentStatus.Cancelled, ct))
            return Err.Conflict("На этот день уже есть записи. Перенесите их, затем меняйте время или врача");

        schedule.EmployeeId = dto.EmployeeId;
        schedule.RoomId = dto.RoomId;
        schedule.WorkDate = dto.WorkDate;
        schedule.StartTime = dto.StartTime;
        schedule.EndTime = dto.EndTime;
        schedule.SlotMinutes = dto.SlotMinutes;
        await db.SaveChangesAsync(ct);

        var result = await db.Schedules.AsNoTracking().Where(s => s.Id == id).Select(Projections.Schedules).FirstAsync(ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> Delete(int id, MyDbContext db, CancellationToken ct)
    {
        if (await db.Appointments.AnyAsync(a => a.ScheduleId == id, ct))
            return Err.Conflict("По этому расписанию уже были записи, удалить нельзя. Отмените записи или измените кабинет");

        var deleted = await db.Schedules.Where(s => s.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Err.NotFound("Расписание не найдено") : Results.NoContent();
    }

    // GET /api/slots?date=2026-10-07&employeeId=2   или   ?date=2026-10-07&specialtyId=1
    private static async Task<IResult> Slots(DateOnly date, int? employeeId, int? specialtyId, MyDbContext db, CancellationToken ct)
    {
        if (date < Clock.Today) return Results.Ok(Array.Empty<SlotDto>());

        var schedules = await db.Schedules.AsNoTracking()
            .Where(s => s.WorkDate == date
                     && s.Employee.IsActive
                     && (employeeId == null || s.EmployeeId == employeeId)
                     && (specialtyId == null || s.Employee.SpecialtyId == specialtyId))
            .Select(s => new
            {
                s.Id,
                s.EmployeeId,
                DoctorName = s.Employee.LastName + " " + s.Employee.FirstName,
                s.StartTime,
                s.EndTime,
                s.SlotMinutes,
                Taken = s.Appointments
                    .Where(a => a.Status != AppointmentStatus.Cancelled)
                    .Select(a => a.StartTime)
                    .ToList()
            })
            .ToListAsync(ct);

        var now = Clock.Now;
        var slots = schedules
            .SelectMany(s => SlotRules.All(s.StartTime, s.EndTime, s.SlotMinutes)
                .Where(t => !s.Taken.Contains(t) && date.ToDateTime(t) > now)
                .Select(t => new SlotDto(s.Id, s.EmployeeId, s.DoctorName, date, t)))
            .OrderBy(x => x.StartTime).ThenBy(x => x.DoctorName)
            .ToList();

        return Results.Ok(slots);
    }
}
