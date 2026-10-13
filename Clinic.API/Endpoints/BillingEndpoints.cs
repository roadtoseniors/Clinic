namespace Clinic.API;

// Оказанные услуги и счета (регистратура / касса)
public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/visits/{id:int}/services", VisitServices).RequirePermission(Perm.ServiceRender, Perm.InvoiceManage);
        api.MapPost("/visits/{id:int}/services", AddService).RequirePermission(Perm.ServiceRender);
        api.MapDelete("/visit-services/{id:int}", DeleteService).RequirePermission(Perm.ServiceRender);
        api.MapGet("/patients/{id:int}/unbilled-services", Unbilled).RequirePermission(Perm.InvoiceManage);

        api.MapGet("/invoices", List).RequirePermission(Perm.InvoiceManage);
        api.MapGet("/invoices/{id:int}", Get).RequirePermission(Perm.InvoiceManage);
        api.MapPost("/invoices", Create).RequirePermission(Perm.InvoiceManage);
        api.MapPost("/invoices/{id:int}/pay", Pay).RequirePermission(Perm.InvoiceManage);
        api.MapPost("/invoices/{id:int}/cancel", Cancel).RequirePermission(Perm.InvoiceManage);
    }

    // ===================== Оказанные услуги =====================
    private static async Task<IResult> VisitServices(int id, MyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.VisitServices.AsNoTracking()
            .Where(vs => vs.VisitId == id)
            .OrderBy(vs => vs.RenderedAt)
            .Select(Projections.VisitServices)
            .ToListAsync(ct));

    // POST /api/visits/{id}/services  { "serviceId": 3, "quantity": 1 }
    // Цену берёт сервер из прайса: в visit_service она фиксируется на момент оказания
    private static async Task<IResult> AddService(int id, VisitServiceInput dto, MyDbContext db, CancellationToken ct)
    {
        if (dto.Quantity < 1 || dto.Quantity > 100) return Err.BadRequest("Количество: от 1 до 100");
        if (!await db.Visits.AnyAsync(v => v.Id == id, ct)) return Err.NotFound("Приём не найден");

        var price = await db.Services.Where(s => s.Id == dto.ServiceId && s.IsActive).Select(s => (decimal?)s.Price).FirstOrDefaultAsync(ct);
        if (price is null) return Err.BadRequest("Услуга не найдена или выключена");

        var line = new VisitService
        {
            VisitId = id,
            ServiceId = dto.ServiceId,
            Quantity = dto.Quantity,
            Price = price.Value,
            RenderedAt = Clock.Now
        };
        db.VisitServices.Add(line);
        await db.SaveChangesAsync(ct);

        var result = await db.VisitServices.AsNoTracking().Where(vs => vs.Id == line.Id).Select(Projections.VisitServices).FirstAsync(ct);
        return Results.Created($"/api/visit-services/{line.Id}", result);
    }

    // Удалить можно только услугу, которая ещё не попала в счёт
    private static async Task<IResult> DeleteService(int id, MyDbContext db, CancellationToken ct)
    {
        var line = await db.VisitServices.AsNoTracking().Where(vs => vs.Id == id).Select(vs => new { vs.InvoiceId }).FirstOrDefaultAsync(ct);
        if (line is null) return Err.NotFound("Услуга не найдена");
        if (line.InvoiceId is not null) return Err.Conflict("Услуга уже в счёте. Сначала отмените счёт");

        await db.VisitServices.Where(vs => vs.Id == id).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    // GET /api/patients/{id}/unbilled-services — услуги пациента, которые ещё не в счёте
    private static async Task<IResult> Unbilled(int id, MyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.VisitServices.AsNoTracking()
            .Where(vs => vs.InvoiceId == null && vs.Visit.Appointment.PatientId == id)
            .OrderBy(vs => vs.RenderedAt)
            .Select(Projections.VisitServices)
            .ToListAsync(ct));

    // ===================== Счета =====================

    // GET /api/invoices?status=не оплачен&patientId=&from=&to=&page=1
    private static async Task<IResult> List(string? status, int? patientId, DateOnly? from, DateOnly? to,
        int? page, int? pageSize, MyDbContext db, CancellationToken ct)
    {
        var query = db.Invoices.AsNoTracking();
        if (Text.Clean(status) is string s) query = query.Where(i => i.Status == s);
        if (patientId is int p) query = query.Where(i => i.PatientId == p);
        if (from is DateOnly f)
        {
            var fromDt = f.ToDateTime(TimeOnly.MinValue);
            query = query.Where(i => i.CreatedAt >= fromDt);
        }
        if (to is DateOnly t)
        {
            var toDt = t.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(i => i.CreatedAt < toDt);
        }

        return Results.Ok(await query
            .OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
            .Select(Projections.Invoices)
            .ToPagedAsync(page, pageSize, ct));
    }

    private static async Task<InvoiceDetailsDto?> LoadDetails(MyDbContext db, int id, CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking().Where(i => i.Id == id).Select(Projections.Invoices).FirstOrDefaultAsync(ct);
        if (invoice is null) return null;

        var lines = await db.VisitServices.AsNoTracking()
            .Where(vs => vs.InvoiceId == id)
            .OrderBy(vs => vs.RenderedAt)
            .Select(Projections.VisitServices)
            .ToListAsync(ct);
        return new InvoiceDetailsDto(invoice, lines);
    }

    private static async Task<IResult> Get(int id, MyDbContext db, CancellationToken ct)
    {
        var details = await LoadDetails(db, id, ct);
        return details is null ? Err.NotFound("Счёт не найден") : Results.Ok(details);
    }

    // POST /api/invoices  { "patientId": 3 }  или  { "patientId": 3, "visitServiceIds": [5, 6] }
    private static async Task<IResult> Create(CreateInvoiceDto dto, MyDbContext db, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == dto.PatientId, ct)) return Err.NotFound("Пациент не найден");

        var query = db.VisitServices.Where(vs => vs.InvoiceId == null && vs.Visit.Appointment.PatientId == dto.PatientId);
        var ids = dto.VisitServiceIds?.Distinct().ToList();
        if (ids is { Count: > 0 }) query = query.Where(vs => ids.Contains(vs.Id));

        var lines = await query.ToListAsync(ct);
        if (lines.Count == 0) return Err.BadRequest("Нет услуг для выставления счёта");
        if (ids is { Count: > 0 } && lines.Count != ids.Count)
            return Err.BadRequest("Часть услуг не найдена, уже в счёте или относится к другому пациенту");

        var invoice = new Invoice
        {
            PatientId = dto.PatientId,
            CreatedAt = Clock.Now,
            Status = InvoiceStatus.Unpaid
        };
        db.Invoices.Add(invoice);
        foreach (var line in lines) line.Invoice = invoice;
        await db.SaveChangesAsync(ct);   // счёт и привязка услуг — одна транзакция

        return Results.Created($"/api/invoices/{invoice.Id}", await LoadDetails(db, invoice.Id, ct));
    }

    // POST /api/invoices/{id}/pay  { "methodId": 1 }  или для ДМС  { "methodId": 4, "policyId": 12 }
    private static async Task<IResult> Pay(int id, PayInvoiceDto dto, MyDbContext db, CancellationToken ct)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return Err.NotFound("Счёт не найден");
        if (invoice.Status != InvoiceStatus.Unpaid) return Err.Conflict($"Счёт в статусе «{invoice.Status}»");

        var method = await db.PayMethods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == dto.MethodId, ct);
        if (method is null) return Err.BadRequest("Способ оплаты не найден");

        if (method.IsInsurance)
        {
            if (dto.PolicyId is not int policyId) return Err.BadRequest("Для оплаты по ДМС укажите полис");
            var today = Clock.Today;
            var policyOk = await db.PatientPolicies.AnyAsync(p =>
                p.Id == policyId && p.PatientId == invoice.PatientId && p.Kind == PolicyKind.Dms && p.IsActive
                && p.ValidFrom <= today && (p.ValidTo == null || p.ValidTo >= today), ct);
            if (!policyOk) return Err.BadRequest("У пациента нет такого действующего полиса ДМС");
        }
        else if (dto.PolicyId is not null)
        {
            return Err.BadRequest("Полис указывается только при оплате по ДМС");
        }

        // Статус, способ и дата оплаты ставятся вместе — этого требует CHECK в базе
        invoice.Status = InvoiceStatus.Paid;
        invoice.MethodId = method.Id;
        invoice.PolicyId = method.IsInsurance ? dto.PolicyId : null;
        invoice.PaidAt = Clock.Now;
        await db.SaveChangesAsync(ct);

        return Results.Ok(await LoadDetails(db, id, ct));
    }

    // POST /api/invoices/{id}/cancel — отменить неоплаченный счёт, услуги снова доступны для нового счёта
    private static async Task<IResult> Cancel(int id, MyDbContext db, CancellationToken ct)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null) return Err.NotFound("Счёт не найден");
        if (invoice.Status != InvoiceStatus.Unpaid) return Err.Conflict("Отменить можно только неоплаченный счёт");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.VisitServices.Where(vs => vs.InvoiceId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(vs => vs.InvoiceId, (int?)null), ct);
        invoice.Status = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.NoContent();
    }
}
