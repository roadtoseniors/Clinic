namespace Clinic.API;

public static class EmployeeEndpoints
{
    public static void MapEmployeeEndpoints(this RouteGroupBuilder api)
    {
        // Полный список сотрудников (с телефонами) — только персоналу
        api.MapGet("/employees", List).RequireStaff();
        api.MapGet("/employees/{id:int}", Get).RequireStaff();
        api.MapPost("/employees", Create).RequirePermission(Perm.ReferenceManage, Perm.AccountManage);
        api.MapPut("/employees/{id:int}", Update).RequirePermission(Perm.ReferenceManage, Perm.AccountManage);
        api.MapDelete("/employees/{id:int}", Deactivate).RequirePermission(Perm.ReferenceManage, Perm.AccountManage);

        // Список врачей для записи — доступен и пациенту (без телефонов)
        api.MapGet("/doctors", Doctors);
    }

    private static async Task<IResult> List(bool? includeInactive, MyDbContext db, CancellationToken ct)
    {
        var query = db.Employees.AsNoTracking();
        if (includeInactive != true) query = query.Where(e => e.IsActive);
        return Results.Ok(await query
            .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Select(Projections.Employees)
            .ToListAsync(ct));
    }

    private static async Task<IResult> Get(int id, MyDbContext db, CancellationToken ct)
    {
        var employee = await db.Employees.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(Projections.Employees)
            .FirstOrDefaultAsync(ct);
        return employee is null ? Err.NotFound("Сотрудник не найден") : Results.Ok(employee);
    }

    private static async Task<IResult> Doctors(int? specialtyId, MyDbContext db, CancellationToken ct)
    {
        var query = db.Employees.AsNoTracking().Where(e => e.IsActive && e.SpecialtyId != null);
        if (specialtyId is int s) query = query.Where(e => e.SpecialtyId == s);

        return Results.Ok(await query
            .OrderBy(e => e.Specialty!.Name).ThenBy(e => e.LastName)
            .Select(e => new DoctorDto(
                e.Id,
                (e.LastName + " " + e.FirstName + " " + (e.MiddleName ?? "")).Trim(),
                e.SpecialtyId!.Value,
                e.Specialty!.Name))
            .ToListAsync(ct));
    }

    private static async Task<string?> ValidateAsync(EmployeeInput dto, MyDbContext db, CancellationToken ct)
    {
        if (Text.Clean(dto.LastName) is null || Text.Clean(dto.FirstName) is null) return "Фамилия и имя обязательны";
        if (Rules.ValidateContacts(dto.MiddleName,dto.Phone,null,null) is string error) return error;
        if (Clinic.Workspace.InputRules.Error("last_name",dto.LastName) is string lastError) return lastError;
        if (Clinic.Workspace.InputRules.Error("first_name",dto.FirstName) is string firstError) return firstError;
        if (!await db.Posts.AnyAsync(p => p.Id == dto.PostId, ct)) return "Должность не найдена";
        if (dto.SpecialtyId is int s && !await db.Specialties.AnyAsync(x => x.Id == s, ct)) return "Специальность не найдена";
        return null;
    }

    private static async Task<IResult> Create(EmployeeInput dto, MyDbContext db, CancellationToken ct)
    {
        if (await ValidateAsync(dto, db, ct) is string error) return Err.BadRequest(error);

        var employee = new Employee();
        Apply(employee, dto);
        db.Employees.Add(employee);
        await db.SaveChangesAsync(ct);

        var result = await db.Employees.AsNoTracking().Where(e => e.Id == employee.Id).Select(Projections.Employees).FirstAsync(ct);
        return Results.Created($"/api/employees/{employee.Id}", result);
    }

    private static async Task<IResult> Update(int id, EmployeeInput dto, MyDbContext db, CancellationToken ct)
    {
        if (await ValidateAsync(dto, db, ct) is string error) return Err.BadRequest(error);
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null) return Err.NotFound("Сотрудник не найден");

        Apply(employee, dto);
        await db.SaveChangesAsync(ct);

        var result = await db.Employees.AsNoTracking().Where(e => e.Id == id).Select(Projections.Employees).FirstAsync(ct);
        return Results.Ok(result);
    }

    // Увольнение: сотрудник становится неактивным, его аккаунт блокируется.
    // Удалять нельзя: на сотрудника ссылаются расписание, приёмы, журнал.
    private static async Task<IResult> Deactivate(int id, MyDbContext db, CancellationToken ct)
    {
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (employee is null) return Err.NotFound("Сотрудник не найден");

        var futurePlanned = await db.Appointments.AnyAsync(a =>
            a.Schedule.EmployeeId == id
            && a.Schedule.WorkDate >= Clock.Today
            && a.Status == AppointmentStatus.Planned, ct);
        if (futurePlanned)
            return Err.Conflict("У сотрудника есть будущие записи пациентов. Сначала перенесите или отмените их");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        employee.IsActive = false;
        await db.SaveChangesAsync(ct);
        await db.Accounts.Where(a => a.EmployeeId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsActive, false), ct);
        await tx.CommitAsync(ct);

        return Results.NoContent();
    }

    private static void Apply(Employee e, EmployeeInput dto)
    {
        e.LastName = dto.LastName.Trim();
        e.FirstName = dto.FirstName.Trim();
        e.MiddleName = Text.Clean(dto.MiddleName);
        e.PostId = dto.PostId;
        e.SpecialtyId = dto.SpecialtyId;
        e.Phone = Text.Clean(dto.Phone);
        e.IsActive = dto.IsActive;
    }
}
