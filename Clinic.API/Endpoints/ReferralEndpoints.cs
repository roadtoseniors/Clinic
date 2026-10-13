namespace Clinic.API;

// Исследования: очередь направлений, ввод заключения, файлы результатов (врач и лаборант)
public static class ReferralEndpoints
{
    private const long MaxFileBytes = 10 * 1024 * 1024;   // 10 МБ, как ограничение в базе
    private static readonly string[] AllowedTypes = { "application/pdf", "image/jpeg", "image/png" };

    public static void MapReferralEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/referrals", Queue).RequirePermission(Perm.ReferralView);
        api.MapGet("/referrals/{id:int}", Get).RequirePermission(Perm.ReferralView, Perm.MedcardView);
        api.MapPut("/referrals/{id:int}/result", SaveResult).RequirePermission(Perm.ReferralResult);

        api.MapGet("/referrals/{id:int}/files", Files).RequirePermission(Perm.ReferralView, Perm.MedcardView);
        api.MapPost("/referrals/{id:int}/files", Upload)
            .RequirePermission(Perm.ReferralResult)
            .DisableAntiforgery();   // для загрузки файлов в minimal API (.NET 8+), у нас JWT, а не cookies
        api.MapGet("/referral-files/{id:int}", Download).RequirePermission(Perm.ReferralView, Perm.MedcardView);
        api.MapDelete("/referral-files/{id:int}", DeleteFile).RequirePermission(Perm.ReferralResult);
    }

    // GET /api/referrals?status=выдано&serviceId=&patientId=&page=1
    // По умолчанию — очередь невыполненных направлений, старые первыми. status=all — все.
    private static async Task<IResult> Queue(string? status, int? serviceId, int? patientId, int? page, int? pageSize,
        MyDbContext db, CancellationToken ct)
    {
        var query = db.Referrals.AsNoTracking();
        var wanted = Text.Clean(status) ?? ReferralStatus.Issued;
        if (wanted != "all") query = query.Where(r => r.Status == wanted);
        if (serviceId is int s) query = query.Where(r => r.ServiceId == s);
        if (patientId is int p) query = query.Where(r => r.Visit.Appointment.PatientId == p);

        query = wanted == ReferralStatus.Issued ? query.OrderBy(r => r.Id) : query.OrderByDescending(r => r.Id);

        return Results.Ok(await query.Select(Projections.Referrals).ToPagedAsync(page, pageSize, ct));
    }

    private static async Task<IResult> Get(int id, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var referral = await db.Referrals.AsNoTracking().Where(r => r.Id == id).Select(Projections.Referrals).FirstOrDefaultAsync(ct);
        if (referral is null) return Err.NotFound("Направление не найдено");

        await audit.LogAsync(user, AuditAction.Select, "referral", id, referral.PatientId, ct);
        return Results.Ok(referral);
    }

    // PUT /api/referrals/{id}/result  { "result": "Заключение..." }
    // Статус «выполнено», исполнитель — текущий сотрудник. Уведомление пациенту создаёт триггер базы.
    private static async Task<IResult> SaveResult(int id, ReferralResultDto dto, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        if (user.GetEmployeeId() is not int employeeId) return Err.Forbidden("Результат вносит сотрудник");
        var text = Text.Clean(dto.Result);
        if (text is null) return Err.BadRequest("Заключение обязательно");

        var referral = await db.Referrals
            .Include(r => r.Visit).ThenInclude(v => v.Appointment)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (referral is null) return Err.NotFound("Направление не найдено");
        if (referral.Status != ReferralStatus.Issued)
            return Err.Conflict($"Направление в статусе «{referral.Status}», результат уже внесён или направление отменено");

        referral.Result = text;
        referral.Status = ReferralStatus.Done;
        referral.DoneAt = Clock.Now;
        referral.ExecutorId = employeeId;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(user, AuditAction.Update, "referral", id, referral.Visit.Appointment.PatientId, ct);

        var result = await db.Referrals.AsNoTracking().Where(r => r.Id == id).Select(Projections.Referrals).FirstAsync(ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> Files(int id, MyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.ReferralFiles.AsNoTracking()
            .Where(f => f.ReferralId == id)
            .OrderBy(f => f.UploadedAt)
            .Select(Projections.ReferralFiles)
            .ToListAsync(ct));

    // POST /api/referrals/{id}/files — multipart/form-data, поле "file" (PDF, JPG, PNG до 10 МБ)
    private static async Task<IResult> Upload(int id, IFormFile file, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        if (file.Length == 0) return Err.BadRequest("Файл пустой");
        if (file.Length > MaxFileBytes) return Err.BadRequest("Файл больше 10 МБ");
        if (!AllowedTypes.Contains(file.ContentType)) return Err.BadRequest("Допустимые форматы: PDF, JPG, PNG");

        var referral = await db.Referrals.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.Status, r.Visit.Appointment.PatientId })
            .FirstOrDefaultAsync(ct);
        if (referral is null) return Err.NotFound("Направление не найдено");
        if (referral.Status == ReferralStatus.Cancelled) return Err.Conflict("Направление отменено");

        using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);

        var entity = new ReferralFile
        {
            ReferralId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = file.ContentType,
            Data = memory.ToArray(),
            UploadedAt = Clock.Now
        };
        db.ReferralFiles.Add(entity);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(user, AuditAction.Insert, "referral_file", entity.Id, referral.PatientId, ct);

        return Results.Created($"/api/referral-files/{entity.Id}",
            new ReferralFileDto(entity.Id, id, entity.FileName, entity.ContentType, entity.Data.Length, entity.UploadedAt));
    }

    // GET /api/referral-files/{id} — скачать файл
    private static async Task<IResult> Download(int id, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var file = await db.ReferralFiles.AsNoTracking()
            .Where(f => f.Id == id)
            .Select(f => new { f.Data, f.ContentType, f.FileName, f.Referral.Visit.Appointment.PatientId })
            .FirstOrDefaultAsync(ct);
        if (file is null) return Err.NotFound("Файл не найден");

        await audit.LogAsync(user, AuditAction.Select, "referral_file", id, file.PatientId, ct);
        return Results.File(file.Data, file.ContentType, file.FileName);
    }

    private static async Task<IResult> DeleteFile(int id, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var patientId = await db.ReferralFiles.Where(f => f.Id == id)
            .Select(f => (int?)f.Referral.Visit.Appointment.PatientId)
            .FirstOrDefaultAsync(ct);
        if (patientId is null) return Err.NotFound("Файл не найден");

        await db.ReferralFiles.Where(f => f.Id == id).ExecuteDeleteAsync(ct);
        await audit.LogAsync(user, AuditAction.Delete, "referral_file", id, patientId, ct);
        return Results.NoContent();
    }
}
