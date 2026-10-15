namespace Clinic.API;

// Журнал обращений к медицинским данным (таблица audit).
// Вызывается ПОСЛЕ успешного сохранения основной операции.
public sealed class AuditService(MyDbContext db, ILogger<AuditService> logger)
{
    public async Task LogAsync(ClaimsPrincipal user, string action, string table, int? recordId, int? patientId, CancellationToken ct)
    {
        if (user.GetAccountId() is not int accountId) return;

        var entry = new Audit
        {
            AccountId = accountId,
            PatientId = patientId,
            ActedAt = Clock.Now,
            Action = action,
            TableName = table,
            RecordId = recordId
        };

        db.Audits.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Основная операция уже сохранена, поэтому ошибку журнала не отдаём клиенту, а пишем в лог
            db.Entry(entry).State = EntityState.Detached;
            logger.LogError(ex, "Не удалось записать аудит: {Action} {Table} {RecordId}", action, table, recordId);
        }
    }
}
