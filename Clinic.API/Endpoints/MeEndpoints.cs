namespace Clinic.API;

// Общие функции любого вошедшего пользователя и личный кабинет пациента (/api/my/...).
// В кабинете id пациента берётся из токена, поэтому увидеть чужие данные невозможно.
public static class MeEndpoints
{
    public static void MapMeEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", GetMe);
        api.MapPut("/me/theme", SetTheme);
        api.MapPut("/me/password", ChangePassword);

        var my = api.MapGroup("/my").RequirePermission(Perm.OwnView);
        my.MapGet("/profile", Profile);
        my.MapGet("/appointments", Appointments);
        my.MapGet("/medcard", Medcard);
        my.MapGet("/referrals", Referrals);
        my.MapGet("/referral-files/{id:int}", DownloadFile);
        my.MapGet("/invoices", Invoices);
        my.MapGet("/documents", Documents);
        my.MapGet("/notifications", Notifications);
        my.MapPost("/notifications/{id:int}/read", ReadNotification);
        my.MapPost("/notifications/read-all", ReadAllNotifications);
    }

    private static IResult NotPatient() => Err.Forbidden("Раздел доступен только пациенту");

    // GET /api/me — данные текущего аккаунта и его права (для меню клиента)
    private static async Task<IResult> GetMe(ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        var accountId = user.GetAccountId();
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(Projections.AccountsFull)
            .FirstOrDefaultAsync(ct);
        if (account is null) return Err.NotFound("Аккаунт не найден");

        var permissions = user.FindAll(Perm.ClaimType).Select(c => c.Value).ToList();
        return Results.Ok(new MeDto(account, permissions));
    }

    // PUT /api/me/theme  { "theme": "dark" }
    private static async Task<IResult> SetTheme(ThemeDto dto, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        if (dto.Theme is not (Theme.Light or Theme.Dark))
            return Err.BadRequest("Тема: light или dark");

        var accountId = user.GetAccountId();
        await db.Accounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UserTheme, dto.Theme), ct);
        return Results.NoContent();
    }

    // PUT /api/me/password
    private static async Task<IResult> ChangePassword(ChangePasswordDto dto, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        if (Passwords.Validate(dto.NewPassword) is string error)
            return Err.BadRequest(error);

        var accountId = user.GetAccountId();
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null) return Err.NotFound("Аккаунт не найден");
        if (!Passwords.Verify(dto.OldPassword ?? "", account.PasswordHash))
            return Err.BadRequest("Текущий пароль указан неверно");

        account.PasswordHash = Passwords.Hash(dto.NewPassword);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // GET /api/my/profile — личные данные, полисы, прикрепление
    private static async Task<IResult> Profile(ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        if (user.GetPatientId() is not int patientId) return NotPatient();
        var profile = await MedcardQueries.ProfileAsync(db, patientId, ct);
        return profile is null ? Err.NotFound("Пациент не найден") : Results.Ok(profile);
    }

    // GET /api/my/appointments
    private static async Task<IResult> Appointments(ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        if (user.GetPatientId() is not int patientId) return NotPatient();
        var list = await db.Appointments.AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.Schedule.WorkDate).ThenBy(a => a.StartTime)
            .Select(Projections.MyAppointments)
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    // GET /api/my/medcard — история приёмов с диагнозами, назначениями, направлениями
    private static async Task<IResult> Medcard(ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        if (user.GetPatientId() is not int patientId) return NotPatient();
        var profile = await MedcardQueries.ProfileAsync(db, patientId, ct);
        if (profile is null) return Err.NotFound("Пациент не найден");

        var visits = await MedcardQueries.VisitsAsync(db, patientId, ct);
        await audit.LogAsync(user, AuditAction.Select, "visit", null, patientId, ct);
        return Results.Ok(new MedcardDto(profile, visits));
    }

    // GET /api/my/referrals — направления и результаты исследований
    private static async Task<IResult> Referrals(ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        if (user.GetPatientId() is not int patientId) return NotPatient();
        var list = await db.Referrals.AsNoTracking()
            .Where(r => r.Visit.Appointment.PatientId == patientId)
            .OrderByDescending(r => r.Id)
            .Select(Projections.Referrals)
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    // GET /api/my/referral-files/{id} — скачать файл результата своего исследования
    private static async Task<IResult> DownloadFile(int id, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        if (user.GetPatientId() is not int patientId) return NotPatient();
        var file = await db.ReferralFiles.AsNoTracking()
            .Where(f => f.Id == id && f.Referral.Visit.Appointment.PatientId == patientId)
            .Select(f => new { f.Data, f.ContentType, f.FileName })
            .FirstOrDefaultAsync(ct);
        if (file is null) return Err.NotFound("Файл не найден");

        await audit.LogAsync(user, AuditAction.Select, "referral_file", id, patientId, ct);
        return Results.File(file.Data, file.ContentType, file.FileName);
    }

    // GET /api/my/invoices
    private static async Task<IResult> Invoices(ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        if (user.GetPatientId() is not int patientId) return NotPatient();
        var list = await db.Invoices.AsNoTracking()
            .Where(i => i.PatientId == patientId)
            .OrderByDescending(i => i.CreatedAt)
            .Select(Projections.Invoices)
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    // GET /api/my/documents — согласия, договоры, справки
    private static async Task<IResult> Documents(ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        if (user.GetPatientId() is not int patientId) return NotPatient();
        var list = await db.Documents.AsNoTracking()
            .Where(d => d.PatientId == patientId)
            .OrderByDescending(d => d.IssuedAt)
            .Select(Projections.Documents)
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    // GET /api/my/notifications — непрочитанные первыми
    private static async Task<IResult> Notifications(ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        if (user.GetPatientId() is not int patientId) return NotPatient();
        var list = await db.Notifications.AsNoTracking()
            .Where(n => n.PatientId == patientId)
            .OrderBy(n => n.IsRead).ThenByDescending(n => n.CreatedAt)
            .Take(50)
            .Select(Projections.Notifications)
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    // POST /api/my/notifications/{id}/read
    private static async Task<IResult> ReadNotification(int id, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        if (user.GetPatientId() is not int patientId) return NotPatient();
        var updated = await db.Notifications
            .Where(n => n.Id == id && n.PatientId == patientId)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
        return updated == 0 ? Err.NotFound("Уведомление не найдено") : Results.NoContent();
    }

    // POST /api/my/notifications/read-all
    private static async Task<IResult> ReadAllNotifications(ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        if (user.GetPatientId() is not int patientId) return NotPatient();
        await db.Notifications
            .Where(n => n.PatientId == patientId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
        return Results.NoContent();
    }
}
