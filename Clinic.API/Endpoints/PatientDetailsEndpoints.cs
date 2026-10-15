namespace Clinic.API;

public record PatientDetailsInput(PatientInput Patient, string OmsNumber, int OmsCompanyId,
    string? DmsNumber, int? DmsCompanyId, int? DistrictId, int? EmployeeId);

public static partial class PatientEndpoints
{
    private static async Task<IResult> SaveDetails(int? id, PatientDetailsInput dto, ClaimsPrincipal user,
        MyDbContext db, AuditService audit, CancellationToken ct)
    {
        var p = dto.Patient;
        if (p is null) return Err.BadRequest("Заполните данные пациента");
        if (await ValidatePatientAsync(p.LastName,p.FirstName,p.BirthDate,p.Gender,Text.Clean(p.Snils),id,db,ct,p.MiddleName,p.Phone,p.Address) is string personError)
            return Err.BadRequest(personError);
        if (Clinic.Workspace.InputRules.Error("oms_policy",dto.OmsNumber) is string omsError || string.IsNullOrWhiteSpace(dto.OmsNumber))
            return Err.BadRequest(Clinic.Workspace.InputRules.Error("oms_policy",dto.OmsNumber) ?? "Укажите полис ОМС");
        if (Clinic.Workspace.InputRules.Error("dms_policy",dto.DmsNumber) is string dmsError) return Err.BadRequest(dmsError);
        if ((dto.DistrictId is null) != (dto.EmployeeId is null)) return Err.BadRequest("Выберите участок и участкового врача");
        if (dto.DistrictId.HasValue && !await db.Districts.AnyAsync(d=>d.Id==dto.DistrictId,ct)) return Err.BadRequest("Участок не найден");
        if (dto.EmployeeId.HasValue && !await db.Employees.AnyAsync(e=>e.Id==dto.EmployeeId && e.IsActive && e.SpecialtyId!=null,ct)) return Err.BadRequest("Врач не найден или неактивен");
        var policies = new[] { (Kind:PolicyKind.Oms, Number:Text.Clean(dto.OmsNumber), Company:(int?)dto.OmsCompanyId),
            (Kind:PolicyKind.Dms,Number:Text.Clean(dto.DmsNumber),Company:dto.DmsCompanyId) };
        foreach (var policy in policies.Where(x=>x.Number is not null))
        {
            if (!await db.InsuranceCompanies.AnyAsync(c=>c.Id==policy.Company && c.IsActive,ct)) return Err.BadRequest("Выберите действующую страховую компанию");
            if (await db.PatientPolicies.AnyAsync(pp=>pp.Number==policy.Number && !(pp.PatientId==id && pp.Kind==policy.Kind && pp.IsActive),ct))
                return Err.Conflict("Полис с таким номером уже зарегистрирован");
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var patient = id is null ? new Patient { CreatedDate=Clock.Today,IsActive=true } : await db.Patients.FirstOrDefaultAsync(x=>x.Id==id,ct);
        if (patient is null) return Err.NotFound("Пациент не найден");
        patient.LastName=p.LastName.Trim(); patient.FirstName=p.FirstName.Trim(); patient.MiddleName=Text.Clean(p.MiddleName);
        patient.BirthDate=p.BirthDate; patient.Gender=Rules.NormalizeGender(p.Gender); patient.Snils=Text.Clean(p.Snils);
        patient.Phone=Text.Clean(p.Phone); patient.Address=Text.Clean(p.Address);
        if(id is null) db.Patients.Add(patient);
        await db.SaveChangesAsync(ct);
        foreach(var policy in policies)
        {
            var current=await db.PatientPolicies.Where(x=>x.PatientId==patient.Id && x.Kind==policy.Kind && x.IsActive).ToListAsync(ct);
            var same=current.FirstOrDefault(x=>x.Number==policy.Number);
            if(same is not null) { same.CompanyId=policy.Company!.Value; continue; }
            foreach(var old in current) { old.IsActive=false;old.ValidTo=Clock.Today; }
            await db.SaveChangesAsync(ct);
            if(policy.Number is not null) db.PatientPolicies.Add(new PatientPolicy { PatientId=patient.Id,Kind=policy.Kind,Number=policy.Number,
                CompanyId=policy.Company!.Value,ValidFrom=Clock.Today,IsActive=true });
        }
        var attached=await db.PatientAttachments.Where(a=>a.PatientId==patient.Id && a.AttachedTo==null).ToListAsync(ct);
        if(!attached.Any(a=>a.DistrictId==dto.DistrictId && a.EmployeeId==dto.EmployeeId))
        {
            foreach(var old in attached)old.AttachedTo=Clock.Today;
            await db.SaveChangesAsync(ct);
            if(dto.DistrictId.HasValue)db.PatientAttachments.Add(new PatientAttachment { PatientId=patient.Id,DistrictId=dto.DistrictId.Value,
                EmployeeId=dto.EmployeeId!.Value,AttachedFrom=Clock.Today });
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await audit.LogAsync(user,id.HasValue?AuditAction.Update:AuditAction.Insert,"patient",patient.Id,patient.Id,ct);
        return Results.Ok(await db.Patients.AsNoTracking().Where(x=>x.Id==patient.Id).Select(Projections.Patients).SingleAsync(ct));
    }
}
