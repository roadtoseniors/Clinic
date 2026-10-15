namespace Clinic.API;

// Регистратура: пациенты, полисы, прикрепление к участку, документы
public static partial class PatientEndpoints
{
    public static void MapPatientEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/patients/details", (PatientDetailsInput dto, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct) => SaveDetails(null,dto,user,db,audit,ct)).RequirePermission(Perm.PatientManage);
        api.MapPut("/patients/{id:int}/details", (int id, PatientDetailsInput dto, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct) => SaveDetails(id,dto,user,db,audit,ct)).RequirePermission(Perm.PatientManage);
        api.MapGet("/patients", Search).RequirePermission(Perm.PatientView);
        api.MapGet("/patients/{id:int}", Get).RequirePermission(Perm.PatientView, Perm.MedcardView);
        api.MapPost("/patients", Create).RequirePermission(Perm.PatientManage);
        api.MapPut("/patients/{id:int}", Update).RequirePermission(Perm.PatientManage);
        api.MapDelete("/patients/{id:int}", Deactivate).RequirePermission(Perm.PatientManage);
        api.MapPost("/patients/{id:int}/restore", Restore).RequirePermission(Perm.PatientManage);
        api.MapPost("/patients/{id:int}/account", CreateAccount).RequirePermission(Perm.PatientManage, Perm.AccountManage);

        api.MapGet("/patients/{id:int}/policies", Policies).RequirePermission(Perm.PatientView);
        api.MapPost("/patients/{id:int}/policies", AddPolicy).RequirePermission(Perm.PatientManage);
        api.MapPut("/policies/{id:int}", UpdatePolicy).RequirePermission(Perm.PatientManage);
        api.MapPost("/policies/{id:int}/close", ClosePolicy).RequirePermission(Perm.PatientManage);

        api.MapGet("/patients/{id:int}/attachments", Attachments).RequirePermission(Perm.PatientView);
        api.MapPost("/patients/{id:int}/attachments", Attach).RequirePermission(Perm.PatientManage);

        api.MapGet("/patients/{id:int}/documents", Documents).RequirePermission(Perm.PatientView, Perm.MedcardView);
        api.MapPost("/patients/{id:int}/documents", AddDocument).RequirePermission(Perm.DocumentManage, Perm.VisitManage);
        api.MapGet("/documents/{id:int}", GetDocument).RequirePermission(Perm.PatientView, Perm.MedcardView);
    }

    // ===================== Пациенты =====================

    // GET /api/patients?q=иванов&birthDate=1990-05-01&page=1&pageSize=50
    // q ищет по ФИО, номеру карты, СНИЛС, телефону и номеру полиса
    private static async Task<IResult> Search(string? q, DateOnly? birthDate, bool? includeInactive,
        int? page, int? pageSize, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        var query = MedicalAccess.Patients(db, user).AsNoTracking();
        if (includeInactive != true) query = query.Where(p => p.IsActive);
        if (birthDate is DateOnly bd) query = query.Where(p => p.BirthDate == bd);

        if (Text.Clean(q) is string search)
        {
            var pattern = Text.LikePattern(search);
            query = query.Where(p =>
                EF.Functions.ILike(p.LastName + " " + p.FirstName + " " + (p.MiddleName ?? ""), pattern)
                || EF.Functions.ILike(p.CardNumber, pattern)
                || (p.Snils != null && EF.Functions.ILike(p.Snils, pattern))
                || (p.Phone != null && EF.Functions.ILike(p.Phone, pattern))
                || p.PatientPolicies.Any(pp => EF.Functions.ILike(pp.Number, pattern)));
        }

        return Results.Ok(await query
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName).ThenBy(p => p.Id)
            .Select(Projections.Patients)
            .ToPagedAsync(page, pageSize, ct));
    }

    // GET /api/patients/{id} — карточка с полисами и прикреплением
    private static async Task<IResult> Get(int id, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var profile = await MedcardQueries.ProfileAsync(db, id, ct);
        if (profile is null) return Err.NotFound("Пациент не найден");

        await audit.LogAsync(user, AuditAction.Select, "patient", id, id, ct);
        return Results.Ok(profile);
    }

    private static async Task<string?> ValidatePatientAsync(string lastName, string firstName, DateOnly birthDate, char gender,
        string? snils, int? exceptId, MyDbContext db, CancellationToken ct, string? middleName = null, string? phone = null, string? address = null)
    {
        if (Rules.ValidatePerson(lastName, firstName, birthDate, gender) is string error) return error;
        if (Rules.ValidateContacts(middleName,phone,snils,address) is string contactError) return contactError;
        if (snils is not null && await db.Patients.AnyAsync(p => p.Snils == snils && p.Id != exceptId, ct))
            return "Пациент с таким СНИЛС уже есть";
        return null;
    }

    // POST /api/patients — регистрация пациента регистратором (полис ОМС можно указать сразу)
    private static async Task<IResult> Create(CreatePatientDto dto, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var snils = Text.Clean(dto.Snils);
        if (await ValidatePatientAsync(dto.LastName, dto.FirstName, dto.BirthDate, dto.Gender, snils, null, db, ct, dto.MiddleName, dto.Phone, dto.Address) is string error)
            return Err.BadRequest(error);

        var omsNumber = Text.Clean(dto.OmsNumber);
        if ((omsNumber is null) != (dto.OmsCompanyId is null))
            return Err.BadRequest("Для полиса ОМС укажите и номер, и страховую компанию");
        if (omsNumber is not null)
        {
            if (await db.PatientPolicies.AnyAsync(p => p.Number == omsNumber, ct))
                return Err.Conflict("Полис с таким номером уже есть");
            if (!await db.InsuranceCompanies.AnyAsync(c => c.Id == dto.OmsCompanyId && c.IsActive, ct))
                return Err.BadRequest("Страховая компания не найдена");
        }

        var patient = new Patient
        {
            LastName = dto.LastName.Trim(),
            FirstName = dto.FirstName.Trim(),
            MiddleName = Text.Clean(dto.MiddleName),
            BirthDate = dto.BirthDate,
            Gender = Rules.NormalizeGender(dto.Gender),
            Snils = snils,
            Address = Text.Clean(dto.Address),
            Phone = Text.Clean(dto.Phone),
            CreatedDate = Clock.Today,
            IsActive = true
        };
        db.Patients.Add(patient);

        if (omsNumber is not null)
        {
            db.PatientPolicies.Add(new PatientPolicy
            {
                Patient = patient,
                Kind = PolicyKind.Oms,
                Number = omsNumber,
                CompanyId = dto.OmsCompanyId!.Value,
                ValidFrom = Clock.Today,
                IsActive = true
            });
        }

        await db.SaveChangesAsync(ct);
        await audit.LogAsync(user, AuditAction.Insert, "patient", patient.Id, patient.Id, ct);

        var result = await db.Patients.AsNoTracking().Where(p => p.Id == patient.Id).Select(Projections.Patients).FirstAsync(ct);
        return Results.Created($"/api/patients/{patient.Id}", result);
    }

    // PUT /api/patients/{id}
    private static async Task<IResult> Update(int id, PatientInput dto, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var snils = Text.Clean(dto.Snils);
        if (await ValidatePatientAsync(dto.LastName, dto.FirstName, dto.BirthDate, dto.Gender, snils, id, db, ct, dto.MiddleName, dto.Phone, dto.Address) is string error)
            return Err.BadRequest(error);

        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (patient is null) return Err.NotFound("Пациент не найден");

        patient.LastName = dto.LastName.Trim();
        patient.FirstName = dto.FirstName.Trim();
        patient.MiddleName = Text.Clean(dto.MiddleName);
        patient.BirthDate = dto.BirthDate;
        patient.Gender = Rules.NormalizeGender(dto.Gender);
        patient.Snils = snils;
        patient.Address = Text.Clean(dto.Address);
        patient.Phone = Text.Clean(dto.Phone);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(user, AuditAction.Update, "patient", id, id, ct);

        var result = await db.Patients.AsNoTracking().Where(p => p.Id == id).Select(Projections.Patients).FirstAsync(ct);
        return Results.Ok(result);
    }

    // DELETE /api/patients/{id} — мягкое удаление: пациент неактивен, аккаунт заблокирован, история сохраняется
    private static async Task<IResult> Deactivate(int id, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (patient is null) return Err.NotFound("Пациент не найден");

        var today = Clock.Today;
        if (await db.Appointments.AnyAsync(a => a.PatientId == id && a.Status == AppointmentStatus.Planned && a.Schedule.WorkDate >= today, ct))
            return Err.Conflict("У пациента есть будущие записи. Сначала отмените их");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        patient.IsActive = false;
        await db.SaveChangesAsync(ct);
        await db.Accounts.Where(a => a.PatientId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsActive, false), ct);
        await tx.CommitAsync(ct);

        await audit.LogAsync(user, AuditAction.Delete, "patient", id, id, ct);
        return Results.NoContent();
    }

    // POST /api/patients/{id}/restore — вернуть пациента (аккаунт разблокирует администратор)
    private static async Task<IResult> Restore(int id, MyDbContext db, CancellationToken ct)
    {
        var updated = await db.Patients.Where(p => p.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, true), ct);
        return updated == 0 ? Err.NotFound("Пациент не найден") : Results.NoContent();
    }

    // POST /api/patients/{id}/account — регистратор создаёт пациенту вход в личный кабинет
    private static async Task<IResult> CreateAccount(int id, NewAccountInput dto, MyDbContext db, CancellationToken ct)
    {
        var roleId = await db.Roles.Where(r => r.Code == RoleCode.Patient).Select(r => (int?)r.Id).FirstOrDefaultAsync(ct);
        if (roleId is null) return Results.Problem(title: "В базе нет роли пациента", statusCode: 500);

        var (error, account) = await AdminEndpoints.CreateAccountAsync(db, dto.Login, dto.Password, roleId.Value, null, id, ct);
        return error ?? Results.Created($"/api/accounts/{account!.Id}", account);
    }

    // ===================== Полисы =====================
    private static async Task<IResult> Policies(int id, MyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.PatientPolicies.AsNoTracking()
            .Where(p => p.PatientId == id)
            .OrderByDescending(p => p.IsActive).ThenBy(p => p.Kind).ThenByDescending(p => p.ValidFrom)
            .Select(Projections.Policies)
            .ToListAsync(ct));

    private static async Task<string?> ValidatePolicyAsync(string? number, int companyId, DateOnly from, DateOnly? to,
        int? exceptId, MyDbContext db, CancellationToken ct)
    {
        if (number is null) return "Номер полиса обязателен";
        if (to is DateOnly t && t < from) return "Дата окончания раньше даты начала";
        if (!await db.InsuranceCompanies.AnyAsync(c => c.Id == companyId && c.IsActive, ct)) return "Страховая компания не найдена";
        if (await db.PatientPolicies.AnyAsync(p => p.Number == number && p.Id != exceptId, ct)) return "Полис с таким номером уже есть";
        return null;
    }

    // POST /api/patients/{id}/policies — новый полис; прежний действующий того же вида закрывается
    private static async Task<IResult> AddPolicy(int id, PolicyInput dto, MyDbContext db, CancellationToken ct)
    {
        var kind = Text.Clean(dto.Kind)?.ToUpperInvariant();
        if (kind is null || !PolicyKind.All.Contains(kind)) return Err.BadRequest("Вид полиса: ОМС или ДМС");

        var number = Text.Clean(dto.Number);
        if (Clinic.Workspace.InputRules.Error(kind == PolicyKind.Oms ? "oms_policy" : "dms_policy", number) is string formatError) return Err.BadRequest(formatError);
        if (await ValidatePolicyAsync(number, dto.CompanyId, dto.ValidFrom, dto.ValidTo, null, db, ct) is string error)
            return Err.BadRequest(error);
        if (!await db.Patients.AnyAsync(p => p.Id == id && p.IsActive, ct))
            return Err.NotFound("Пациент не найден");

        // Два сохранения в одной транзакции: сначала закрываем старый полис, потом добавляем новый
        // (у пациента может быть только один действующий полис каждого вида — так требует база)
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var current = await db.PatientPolicies
            .Where(p => p.PatientId == id && p.Kind == kind && p.IsActive)
            .ToListAsync(ct);
        foreach (var old in current)
        {
            old.IsActive = false;
            old.ValidTo ??= Clock.Today;
        }
        await db.SaveChangesAsync(ct);

        var policy = new PatientPolicy
        {
            PatientId = id,
            Kind = kind,
            Number = number!,
            CompanyId = dto.CompanyId,
            ValidFrom = dto.ValidFrom,
            ValidTo = dto.ValidTo,
            IsActive = true
        };
        db.PatientPolicies.Add(policy);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var result = await db.PatientPolicies.AsNoTracking().Where(p => p.Id == policy.Id).Select(Projections.Policies).FirstAsync(ct);
        return Results.Created($"/api/policies/{policy.Id}", result);
    }

    // PUT /api/policies/{id} — исправить номер, страховую или сроки (вид полиса не меняется)
    private static async Task<IResult> UpdatePolicy(int id, PolicyUpdateInput dto, MyDbContext db, CancellationToken ct)
    {
        var number = Text.Clean(dto.Number);
        if (await ValidatePolicyAsync(number, dto.CompanyId, dto.ValidFrom, dto.ValidTo, id, db, ct) is string error)
            return Err.BadRequest(error);

        var policy = await db.PatientPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null) return Err.NotFound("Полис не найден");
        if (Clinic.Workspace.InputRules.Error(policy.Kind == PolicyKind.Oms ? "oms_policy" : "dms_policy", number) is string formatError) return Err.BadRequest(formatError);

        policy.Number = number!;
        policy.CompanyId = dto.CompanyId;
        policy.ValidFrom = dto.ValidFrom;
        policy.ValidTo = dto.ValidTo;
        await db.SaveChangesAsync(ct);

        var result = await db.PatientPolicies.AsNoTracking().Where(p => p.Id == id).Select(Projections.Policies).FirstAsync(ct);
        return Results.Ok(result);
    }

    // POST /api/policies/{id}/close — полис перестал действовать
    private static async Task<IResult> ClosePolicy(int id, MyDbContext db, CancellationToken ct)
    {
        var policy = await db.PatientPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null) return Err.NotFound("Полис не найден");
        if (!policy.IsActive) return Err.Conflict("Полис уже закрыт");

        policy.IsActive = false;
        var today = Clock.Today;
        if (policy.ValidTo is null || policy.ValidTo > today)
            policy.ValidTo = today < policy.ValidFrom ? policy.ValidFrom : today;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ===================== Прикрепление к участку и врачу =====================
    private static async Task<IResult> Attachments(int id, MyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.PatientAttachments.AsNoTracking()
            .Where(a => a.PatientId == id)
            .OrderByDescending(a => a.AttachedFrom).ThenByDescending(a => a.Id)
            .Select(Projections.Attachments)
            .ToListAsync(ct));

    // POST /api/patients/{id}/attachments — прикрепить; прежнее прикрепление закрывается сегодняшней датой
    private static async Task<IResult> Attach(int id, AttachmentInput dto, MyDbContext db, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == id && p.IsActive, ct)) return Err.NotFound("Пациент не найден");
        if (!await db.Districts.AnyAsync(d => d.Id == dto.DistrictId, ct)) return Err.BadRequest("Участок не найден");
        if (!await db.Employees.AnyAsync(e => e.Id == dto.EmployeeId && e.IsActive && e.SpecialtyId != null, ct))
            return Err.BadRequest("Врач не найден или неактивен");

        var current = await db.PatientAttachments
            .Where(a => a.PatientId == id && a.AttachedTo == null)
            .ToListAsync(ct);
        if (current.Any(a => a.DistrictId == dto.DistrictId && a.EmployeeId == dto.EmployeeId))
            return Err.Conflict("Пациент уже прикреплён к этому участку и врачу");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var old in current) old.AttachedTo = Clock.Today;
        await db.SaveChangesAsync(ct);

        var attachment = new PatientAttachment
        {
            PatientId = id,
            DistrictId = dto.DistrictId,
            EmployeeId = dto.EmployeeId,
            AttachedFrom = Clock.Today
        };
        db.PatientAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var result = await db.PatientAttachments.AsNoTracking().Where(a => a.Id == attachment.Id).Select(Projections.Attachments).FirstAsync(ct);
        return Results.Created($"/api/patients/{id}/attachments", result);
    }

    // ===================== Документы (согласия, договоры, справки) =====================
    private static async Task<IResult> Documents(int id, MyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Documents.AsNoTracking()
            .Where(d => d.PatientId == id)
            .OrderByDescending(d => d.IssuedAt)
            .Select(Projections.Documents)
            .ToListAsync(ct));

    private static async Task<IResult> GetDocument(int id, MyDbContext db, CancellationToken ct)
    {
        var document = await db.Documents.AsNoTracking().Where(d => d.Id == id).Select(Projections.Documents).FirstOrDefaultAsync(ct);
        return document is null ? Err.NotFound("Документ не найден") : Results.Ok(document);
    }

    // POST /api/patients/{id}/documents
    // Регистратор оформляет согласия и договоры (без visitId); врач — справки по своему приёму (с visitId).
    private static async Task<IResult> AddDocument(int id, DocumentInput dto, ClaimsPrincipal user, MyDbContext db, AuditService audit, CancellationToken ct)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == id, ct)) return Err.NotFound("Пациент не найден");
        if (!await db.DocumentTypes.AnyAsync(t => t.Id == dto.TypeId, ct)) return Err.BadRequest("Тип документа не найден");

        if (dto.VisitId is int visitId)
        {
            var visit = await db.Visits.AsNoTracking()
                .Where(v => v.Id == visitId)
                .Select(v => new { v.Appointment.PatientId, v.Appointment.Schedule.EmployeeId })
                .FirstOrDefaultAsync(ct);
            if (visit is null || visit.PatientId != id) return Err.BadRequest("Приём не найден у этого пациента");

            // Врач без права document.manage оформляет справки только по своим приёмам
            if (!user.HasPermission(Perm.DocumentManage) && visit.EmployeeId != user.GetEmployeeId())
                return Err.Forbidden("Это приём другого врача");
        }
        else if (!user.HasPermission(Perm.DocumentManage))
        {
            return Err.BadRequest("Врач оформляет документ только по приёму: укажите visitId");
        }

        // Номер документа генерирует база
        var document = new Document
        {
            TypeId = dto.TypeId,
            PatientId = id,
            VisitId = dto.VisitId,
            IssuedAt = Clock.Now,
            IssuedBy = user.GetEmployeeId() ?? throw new InvalidOperationException("Документ оформляет сотрудник"),
            Content = Text.Clean(dto.Content)
        };
        db.Documents.Add(document);
        await db.SaveChangesAsync(ct);
        await audit.LogAsync(user, AuditAction.Insert, "document", document.Id, id, ct);

        var result = await db.Documents.AsNoTracking().Where(d => d.Id == document.Id).Select(Projections.Documents).FirstAsync(ct);
        return Results.Created($"/api/documents/{document.Id}", result);
    }
}
