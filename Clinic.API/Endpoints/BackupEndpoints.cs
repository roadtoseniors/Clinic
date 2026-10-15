namespace Clinic.API;

// Резервное копирование базы (pg_dump / pg_restore). Только с правом backup.manage.
public static class BackupEndpoints
{
    public static void MapBackupEndpoints(this RouteGroupBuilder api)
    {
        var backups = api.MapGroup("/backups").RequirePermission(Perm.BackupManage);
        backups.MapGet("", List);
        backups.MapPost("", Create);
        backups.MapPost("/restore", Restore);
        backups.MapGet("/log", Log);
    }

    // GET /api/backups — файлы копий в папке
    private static IResult List(BackupService backup) => Results.Ok(backup.List());

    // POST /api/backups — создать копию
    private static async Task<IResult> Create(ClaimsPrincipal user, MyDbContext db, BackupService backup, CancellationToken ct)
    {
        var connectionString = db.Database.GetConnectionString();
        if (connectionString is null) return Results.Problem(title: "Не найдена строка подключения", statusCode: 500);

        var (ok, message, fileName) = await backup.CreateAsync(connectionString);
        if (!ok) return Results.Problem(title: message, statusCode: 500);

        await WriteLogAsync(db, user, BackupAction.Backup, fileName!, ct);
        var file = backup.List().First(f => f.FileName == fileName);
        return Results.Created($"/api/backups/{fileName}", file);
    }

    // POST /api/backups/restore  { "fileName": "clinic_20261006_120000.backup" }
    // Заменяет ВСЕ данные базы содержимым копии. Перед восстановлением сделайте свежую копию.
    private static async Task<IResult> Restore(RestoreDto dto, ClaimsPrincipal user, MyDbContext db, BackupService backup, CancellationToken ct)
    {
        var path = backup.ResolveFile(dto.FileName);
        if (path is null) return Err.NotFound("Файл копии не найден");

        var connectionString = db.Database.GetConnectionString();
        if (connectionString is null) return Results.Problem(title: "Не найдена строка подключения", statusCode: 500);

        var (ok, message) = await backup.RestoreAsync(connectionString, path);
        if (!ok) return Results.Problem(title: message, statusCode: 500);

        await RuntimeSchema.ResetSessionsAsync(db,ct);

        // Пишем в журнал уже после восстановления, иначе запись затрётся данными копии
        await WriteLogAsync(db, user, BackupAction.Restore, dto.FileName, ct);
        return Results.Ok(new { message });
    }

    // GET /api/backups/log — журнал копирований и восстановлений
    private static async Task<IResult> Log(MyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.BackupLogs.AsNoTracking()
            .OrderByDescending(b => b.CreatedAt)
            .Take(100)
            .Select(b => new BackupLogDto(b.Id, b.AccountId, b.Action, b.FileName, b.CreatedAt))
            .ToListAsync(ct));

    private static async Task WriteLogAsync(MyDbContext db, ClaimsPrincipal user, string action, string fileName, CancellationToken ct)
    {
        if (user.GetAccountId() is not int accountId || !await db.Accounts.AnyAsync(a=>a.Id==accountId,ct)) return;
        db.BackupLogs.Add(new BackupLog
        {
            AccountId = accountId,
            Action = action,
            FileName = fileName,
            Status = "success",
            CreatedAt = Clock.Now
        });
        await db.SaveChangesAsync(ct);
    }
}
