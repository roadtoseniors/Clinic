namespace Clinic.API;

// Справочники. Читать может любой вошедший, менять — только с правом reference.manage.
public static class ReferenceEndpoints
{
    public static void MapReferenceEndpoints(this RouteGroupBuilder api)
    {
        // Простые справочники (только Id и Name) обслуживает один общий метод
        MapNamed(api, "/posts", db => db.Posts);
        MapNamed(api, "/specialties", db => db.Specialties);
        MapNamed(api, "/servicegroups", db => db.ServiceGroups);
        MapNamed(api, "/drugs", db => db.Drugs);
        MapNamed(api, "/documenttypes", db => db.DocumentTypes);

        api.MapGet("/rooms", GetRooms);
        api.MapPost("/rooms", CreateRoom).RequirePermission(Perm.ReferenceManage);
        api.MapPut("/rooms/{id:int}", UpdateRoom).RequirePermission(Perm.ReferenceManage);
        api.MapDelete("/rooms/{id:int}", DeleteRoom).RequirePermission(Perm.ReferenceManage);

        api.MapGet("/services", GetServices);
        api.MapPost("/services", CreateService).RequirePermission(Perm.ReferenceManage);
        api.MapPut("/services/{id:int}", UpdateService).RequirePermission(Perm.ReferenceManage);
        api.MapDelete("/services/{id:int}", DeactivateService).RequirePermission(Perm.ReferenceManage);

        api.MapGet("/diagnoses", GetDiagnoses);
        api.MapPost("/diagnoses", CreateDiagnosis).RequirePermission(Perm.ReferenceManage);
        api.MapPut("/diagnoses/{code}", UpdateDiagnosis).RequirePermission(Perm.ReferenceManage);
        api.MapDelete("/diagnoses/{code}", DeleteDiagnosis).RequirePermission(Perm.ReferenceManage);

        api.MapGet("/paymethods", GetPayMethods);
        api.MapPost("/paymethods", CreatePayMethod).RequirePermission(Perm.ReferenceManage);
        api.MapPut("/paymethods/{id:int}", UpdatePayMethod).RequirePermission(Perm.ReferenceManage);
        api.MapDelete("/paymethods/{id:int}", DeletePayMethod).RequirePermission(Perm.ReferenceManage);

        api.MapGet("/insurancecompanies", GetCompanies);
        api.MapPost("/insurancecompanies", CreateCompany).RequirePermission(Perm.ReferenceManage);
        api.MapPut("/insurancecompanies/{id:int}", UpdateCompany).RequirePermission(Perm.ReferenceManage);
        api.MapDelete("/insurancecompanies/{id:int}", DeactivateCompany).RequirePermission(Perm.ReferenceManage);

        api.MapGet("/districts", GetDistricts);
        api.MapPost("/districts", CreateDistrict).RequirePermission(Perm.ReferenceManage);
        api.MapPut("/districts/{id:int}", UpdateDistrict).RequirePermission(Perm.ReferenceManage);
        api.MapDelete("/districts/{id:int}", DeleteDistrict).RequirePermission(Perm.ReferenceManage);
    }

    // ===================== Общий код для справочников «Id + Name» =====================
    private static void MapNamed<T>(RouteGroupBuilder api, string route, Func<MyDbContext, DbSet<T>> table)
        where T : class, INamedEntity, new()
    {
        api.MapGet(route, async (MyDbContext db, CancellationToken ct) =>
            Results.Ok(await table(db).AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new NamedDto(x.Id, x.Name))
                .ToListAsync(ct)));

        api.MapPost(route, async (NameInput dto, MyDbContext db, CancellationToken ct) =>
        {
            var name = Text.Clean(dto.Name);
            if (name is null) return Err.BadRequest("Название обязательно");
            if (await table(db).AnyAsync(x => x.Name == name, ct)) return Err.Conflict("Такое название уже есть");

            var entity = new T { Name = name };
            table(db).Add(entity);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api{route}/{entity.Id}", new NamedDto(entity.Id, entity.Name));
        }).RequirePermission(Perm.ReferenceManage);

        api.MapPut(route + "/{id:int}", async (int id, NameInput dto, MyDbContext db, CancellationToken ct) =>
        {
            var name = Text.Clean(dto.Name);
            if (name is null) return Err.BadRequest("Название обязательно");

            var entity = await table(db).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (entity is null) return Err.NotFound();
            if (await table(db).AnyAsync(x => x.Name == name && x.Id != id, ct)) return Err.Conflict("Такое название уже есть");

            entity.Name = name;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new NamedDto(entity.Id, entity.Name));
        }).RequirePermission(Perm.ReferenceManage);

        // Удаление: если запись где-то используется, база вернёт ошибку внешнего ключа → 409
        api.MapDelete(route + "/{id:int}", async (int id, MyDbContext db, CancellationToken ct) =>
        {
            var deleted = await table(db).Where(x => x.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Err.NotFound() : Results.NoContent();
        }).RequirePermission(Perm.ReferenceManage);
    }

    // ===================== Кабинеты =====================
    private static async Task<IResult> GetRooms(MyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Rooms.AsNoTracking()
            .OrderBy(r => r.Floor).ThenBy(r => r.Number)
            .Select(r => new RoomDto(r.Id, r.Number, r.Floor))
            .ToListAsync(ct));

    private static string? ValidateRoom(RoomInput dto) =>
        Text.Clean(dto.Number) is null ? "Номер кабинета обязателен"
        : dto.Floor < -5 || dto.Floor > 50 ? "Неверный этаж"
        : null;

    private static async Task<IResult> CreateRoom(RoomInput dto, MyDbContext db, CancellationToken ct)
    {
        if (ValidateRoom(dto) is string error) return Err.BadRequest(error);
        var number = dto.Number.Trim();
        if (await db.Rooms.AnyAsync(r => r.Number == number, ct)) return Err.Conflict("Такой кабинет уже есть");

        var room = new Room { Number = number, Floor = dto.Floor };
        db.Rooms.Add(room);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/rooms/{room.Id}", new RoomDto(room.Id, room.Number, room.Floor));
    }

    private static async Task<IResult> UpdateRoom(int id, RoomInput dto, MyDbContext db, CancellationToken ct)
    {
        if (ValidateRoom(dto) is string error) return Err.BadRequest(error);
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (room is null) return Err.NotFound("Кабинет не найден");

        var number = dto.Number.Trim();
        if (await db.Rooms.AnyAsync(r => r.Number == number && r.Id != id, ct)) return Err.Conflict("Такой кабинет уже есть");

        room.Number = number;
        room.Floor = dto.Floor;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new RoomDto(room.Id, room.Number, room.Floor));
    }

    private static async Task<IResult> DeleteRoom(int id, MyDbContext db, CancellationToken ct)
    {
        if (await db.Schedules.AnyAsync(s => s.RoomId == id, ct))
            return Err.Conflict("Кабинет используется в расписании, удалить нельзя");
        var deleted = await db.Rooms.Where(r => r.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Err.NotFound("Кабинет не найден") : Results.NoContent();
    }

    // ===================== Услуги (прайс) =====================
    private static async Task<IResult> GetServices(int? groupId, bool? includeInactive, MyDbContext db, CancellationToken ct)
    {
        var query = db.Services.AsNoTracking();
        if (includeInactive != true) query = query.Where(s => s.IsActive);
        if (groupId is int g) query = query.Where(s => s.GroupId == g);

        return Results.Ok(await query
            .OrderBy(s => s.Group.Name).ThenBy(s => s.Name)
            .Select(s => new ServiceDto(s.Id, s.GroupId, s.Group.Name, s.Name, s.Price, s.IsActive))
            .ToListAsync(ct));
    }

    private static async Task<string?> ValidateServiceAsync(ServiceInput dto, MyDbContext db, CancellationToken ct)
    {
        if (Text.Clean(dto.Name) is null) return "Название услуги обязательно";
        if (dto.Price <= 0 || dto.Price > 10_000_000) return "Цена должна быть больше нуля";
        if (!await db.ServiceGroups.AnyAsync(g => g.Id == dto.GroupId, ct)) return "Группа услуг не найдена";
        return null;
    }

    private static async Task<IResult> CreateService(ServiceInput dto, MyDbContext db, CancellationToken ct)
    {
        if (await ValidateServiceAsync(dto, db, ct) is string error) return Err.BadRequest(error);

        var service = new Service { GroupId = dto.GroupId, Name = dto.Name.Trim(), Price = dto.Price, IsActive = dto.IsActive };
        db.Services.Add(service);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/services/{service.Id}", await LoadService(db, service.Id, ct));
    }

    // Цена меняется только для новых оказанных услуг: в visit_service цена зафиксирована на момент оказания
    private static async Task<IResult> UpdateService(int id, ServiceInput dto, MyDbContext db, CancellationToken ct)
    {
        if (await ValidateServiceAsync(dto, db, ct) is string error) return Err.BadRequest(error);
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (service is null) return Err.NotFound("Услуга не найдена");

        service.GroupId = dto.GroupId;
        service.Name = dto.Name.Trim();
        service.Price = dto.Price;
        service.IsActive = dto.IsActive;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadService(db, id, ct));
    }

    // Услуги не удаляем (на них ссылается история), а выключаем
    private static async Task<IResult> DeactivateService(int id, MyDbContext db, CancellationToken ct)
    {
        var updated = await db.Services.Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);
        return updated == 0 ? Err.NotFound("Услуга не найдена") : Results.NoContent();
    }

    private static Task<ServiceDto> LoadService(MyDbContext db, int id, CancellationToken ct) =>
        db.Services.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => new ServiceDto(s.Id, s.GroupId, s.Group.Name, s.Name, s.Price, s.IsActive))
            .FirstAsync(ct);

    // ===================== Диагнозы (МКБ-10) =====================
    private static async Task<IResult> GetDiagnoses(string? q, MyDbContext db, CancellationToken ct)
    {
        var query = db.Diagnoses.AsNoTracking();
        if (Text.Clean(q) is string search)
        {
            var pattern = Text.LikePattern(search);
            query = query.Where(d => EF.Functions.ILike(d.Code, pattern) || EF.Functions.ILike(d.Name, pattern));
        }
        return Results.Ok(await query
            .OrderBy(d => d.Code)
            .Select(d => new DiagnosisDto(d.Code, d.Name))
            .ToListAsync(ct));
    }

    private static async Task<IResult> CreateDiagnosis(DiagnosisDto dto, MyDbContext db, CancellationToken ct)
    {
        var code = Text.Clean(dto.Code)?.ToUpperInvariant();
        var name = Text.Clean(dto.Name);
        if (code is null || name is null) return Err.BadRequest("Код и название обязательны");
        if (await db.Diagnoses.AnyAsync(d => d.Code == code, ct)) return Err.Conflict("Диагноз с таким кодом уже есть");

        db.Diagnoses.Add(new Diagnosis { Code = code, Name = name });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/diagnoses/{code}", new DiagnosisDto(code, name));
    }

    private static async Task<IResult> UpdateDiagnosis(string code, NameInput dto, MyDbContext db, CancellationToken ct)
    {
        var name = Text.Clean(dto.Name);
        if (name is null) return Err.BadRequest("Название обязательно");
        var updated = await db.Diagnoses.Where(d => d.Code == code)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Name, name), ct);
        return updated == 0 ? Err.NotFound("Диагноз не найден") : Results.Ok(new DiagnosisDto(code, name));
    }

    private static async Task<IResult> DeleteDiagnosis(string code, MyDbContext db, CancellationToken ct)
    {
        if (await db.Visits.AnyAsync(v => v.DiagnosisCode == code, ct))
            return Err.Conflict("Диагноз уже указан в приёмах, удалить нельзя");
        var deleted = await db.Diagnoses.Where(d => d.Code == code).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Err.NotFound("Диагноз не найден") : Results.NoContent();
    }

    // ===================== Способы оплаты =====================
    private static async Task<IResult> GetPayMethods(MyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.PayMethods.AsNoTracking()
            .OrderBy(m => m.Id)
            .Select(m => new PayMethodDto(m.Id, m.Name, m.IsInsurance))
            .ToListAsync(ct));

    private static async Task<IResult> CreatePayMethod(PayMethodInput dto, MyDbContext db, CancellationToken ct)
    {
        var name = Text.Clean(dto.Name);
        if (name is null) return Err.BadRequest("Название обязательно");
        if (await db.PayMethods.AnyAsync(m => m.Name == name, ct)) return Err.Conflict("Такой способ оплаты уже есть");

        var method = new PayMethod { Name = name, IsInsurance = dto.IsInsurance };
        db.PayMethods.Add(method);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/paymethods/{method.Id}", new PayMethodDto(method.Id, method.Name, method.IsInsurance));
    }

    private static async Task<IResult> UpdatePayMethod(int id, PayMethodInput dto, MyDbContext db, CancellationToken ct)
    {
        var name = Text.Clean(dto.Name);
        if (name is null) return Err.BadRequest("Название обязательно");
        var method = await db.PayMethods.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (method is null) return Err.NotFound("Способ оплаты не найден");

        method.Name = name;
        method.IsInsurance = dto.IsInsurance;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new PayMethodDto(method.Id, method.Name, method.IsInsurance));
    }

    private static async Task<IResult> DeletePayMethod(int id, MyDbContext db, CancellationToken ct)
    {
        if (await db.Invoices.AnyAsync(i => i.MethodId == id, ct))
            return Err.Conflict("Способ оплаты уже использован в счетах, удалить нельзя");
        var deleted = await db.PayMethods.Where(m => m.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Err.NotFound("Способ оплаты не найден") : Results.NoContent();
    }

    // ===================== Страховые компании =====================
    private static async Task<IResult> GetCompanies(bool? includeInactive, MyDbContext db, CancellationToken ct)
    {
        var query = db.InsuranceCompanies.AsNoTracking();
        if (includeInactive != true) query = query.Where(c => c.IsActive);
        return Results.Ok(await query
            .OrderBy(c => c.Name)
            .Select(c => new InsuranceCompanyDto(c.Id, c.Name, c.Phone, c.IsActive))
            .ToListAsync(ct));
    }

    private static async Task<IResult> CreateCompany(InsuranceCompanyInput dto, MyDbContext db, CancellationToken ct)
    {
        var name = Text.Clean(dto.Name);
        if (name is null) return Err.BadRequest("Название обязательно");
        if (await db.InsuranceCompanies.AnyAsync(c => c.Name == name, ct)) return Err.Conflict("Такая страховая компания уже есть");

        var company = new InsuranceCompany { Name = name, Phone = Text.Clean(dto.Phone), IsActive = dto.IsActive };
        db.InsuranceCompanies.Add(company);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/insurancecompanies/{company.Id}",
            new InsuranceCompanyDto(company.Id, company.Name, company.Phone, company.IsActive));
    }

    private static async Task<IResult> UpdateCompany(int id, InsuranceCompanyInput dto, MyDbContext db, CancellationToken ct)
    {
        var name = Text.Clean(dto.Name);
        if (name is null) return Err.BadRequest("Название обязательно");
        var company = await db.InsuranceCompanies.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (company is null) return Err.NotFound("Страховая компания не найдена");

        company.Name = name;
        company.Phone = Text.Clean(dto.Phone);
        company.IsActive = dto.IsActive;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new InsuranceCompanyDto(company.Id, company.Name, company.Phone, company.IsActive));
    }

    // На страховую ссылаются полисы, поэтому только выключаем
    private static async Task<IResult> DeactivateCompany(int id, MyDbContext db, CancellationToken ct)
    {
        var updated = await db.InsuranceCompanies.Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false), ct);
        return updated == 0 ? Err.NotFound("Страховая компания не найдена") : Results.NoContent();
    }

    // ===================== Участки =====================
    private static async Task<IResult> GetDistricts(MyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Districts.AsNoTracking()
            .OrderBy(d => d.Number)
            .Select(d => new DistrictDto(d.Id, d.Number, d.Name))
            .ToListAsync(ct));

    private static async Task<IResult> CreateDistrict(DistrictInput dto, MyDbContext db, CancellationToken ct)
    {
        var number = Text.Clean(dto.Number);
        var name = Text.Clean(dto.Name);
        if (number is null || name is null) return Err.BadRequest("Номер и название участка обязательны");
        if (await db.Districts.AnyAsync(d => d.Number == number, ct)) return Err.Conflict("Участок с таким номером уже есть");

        var district = new District { Number = number, Name = name };
        db.Districts.Add(district);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/districts/{district.Id}", new DistrictDto(district.Id, district.Number, district.Name));
    }

    private static async Task<IResult> UpdateDistrict(int id, DistrictInput dto, MyDbContext db, CancellationToken ct)
    {
        var number = Text.Clean(dto.Number);
        var name = Text.Clean(dto.Name);
        if (number is null || name is null) return Err.BadRequest("Номер и название участка обязательны");
        var district = await db.Districts.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (district is null) return Err.NotFound("Участок не найден");
        if (await db.Districts.AnyAsync(d => d.Number == number && d.Id != id, ct)) return Err.Conflict("Участок с таким номером уже есть");

        district.Number = number;
        district.Name = name;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new DistrictDto(district.Id, district.Number, district.Name));
    }

    private static async Task<IResult> DeleteDistrict(int id, MyDbContext db, CancellationToken ct)
    {
        if (await db.PatientAttachments.AnyAsync(a => a.DistrictId == id, ct))
            return Err.Conflict("К участку прикреплены пациенты, удалить нельзя");
        var deleted = await db.Districts.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Err.NotFound("Участок не найден") : Results.NoContent();
    }
}
