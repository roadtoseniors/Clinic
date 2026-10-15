using System.Net.Mime;
using Clinic.Context;
using Microsoft.EntityFrameworkCore;

namespace Clinic.API;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/accounts", List).RequirePermission(Perm.AccountManage);
        api.MapPost("/accounts", Create).RequirePermission(Perm.AccountManage);
        api.MapPut("/accounts/{id:int}", Update).RequirePermission(Perm.AccountManage);
        api.MapPost("/accounts/{id:int}/reset-password", ResetPassword).RequirePermission(Perm.AccountManage);
        api.MapDelete("/accounts/{id:int}", Delete).RequirePermission(Perm.AccountManage);

        api.MapGet("/roles", Roles).RequirePermission(Perm.RoleManage, Perm.AccountManage);
        api.MapGet("/permissions", Permissions).RequirePermission(Perm.RoleManage);
        api.MapPut("/roles/{id:int}/permissions", SetPermissions).RequirePermission(Perm.RoleManage);

        api.MapGet("/audits", Audits).RequirePermission(Perm.AuditView);
    }

    // ===================== Аккаунты =====================

    // GET /api/accounts?q=ivan&roleId=2&isActive=true&page=1
    private static async Task<IResult> List(string? q, int? roleId, bool? isActive, int? page, int? pageSize,
        MyDbContext db, CancellationToken ct)
    {
        var query = db.Accounts.AsNoTracking();
        if (roleId is int r) query = query.Where(a => a.RoleId == r);
        if (isActive is bool active) query = query.Where(a => a.IsActive == active);
        if (Text.Clean(q) is string search)
        {
            var pattern = Text.LikePattern(search);
            query = query.Where(a => EF.Functions.ILike(a.Login, pattern)
                || (a.Employee != null && EF.Functions.ILike(a.Employee.LastName, pattern))
                || (a.Patient != null && EF.Functions.ILike(a.Patient.LastName, pattern)));
        }

        return Results.Ok(await query
            .OrderBy(a => a.RoleId).ThenBy(a => a.Login)
            .Select(Projections.Accounts)
            .ToPagedAsync(page, pageSize, ct));
    }

    // Общая логика создания аккаунта (используется и регистратурой для пациентов)
    internal static async Task<(IResult? Error, AccountDto? Account)> CreateAccountAsync(MyDbContext db,
        string? login, string? password, int roleId, int? employeeId, int? patientId, CancellationToken ct)
    {
        var error = Rules.ValidateLogin(login) ?? Passwords.Validate(password);
        if (error is not null) return (Err.BadRequest(error), null);

        var cleanLogin = login!.Trim();
        if (await db.Accounts.AnyAsync(a => a.Login == cleanLogin, ct))
            return (Err.Conflict("Такой логин уже занят"), null);

        var roleCode = await db.Roles.Where(r => r.Id == roleId).Select(r => r.Code).FirstOrDefaultAsync(ct);
        if (roleCode is null) return (Err.BadRequest("Роль не найдена"), null);

        // Аккаунт принадлежит либо пациенту (роль patient), либо сотруднику (остальные роли)
        if (roleCode == RoleCode.Patient)
        {
            if (patientId is not int p || employeeId is not null)
                return (Err.BadRequest("Для роли пациента укажите только patientId"), null);
            if (!await db.Patients.AnyAsync(x => x.Id == p && x.IsActive, ct))
                return (Err.BadRequest("Пациент не найден или неактивен"), null);
            if (await db.Accounts.AnyAsync(a => a.PatientId == p, ct))
                return (Err.Conflict("У пациента уже есть аккаунт"), null);
        }
        else
        {
            if (employeeId is not int e || patientId is not null)
                return (Err.BadRequest("Для роли сотрудника укажите только employeeId"), null);
            if (!await db.Employees.AnyAsync(x => x.Id == e && x.IsActive, ct))
                return (Err.BadRequest("Сотрудник не найден или неактивен"), null);
            if (await db.Accounts.AnyAsync(a => a.EmployeeId == e, ct))
                return (Err.Conflict("У сотрудника уже есть аккаунт"), null);
        }

        var account = new Account
        {
            Login = cleanLogin,
            PasswordHash = Passwords.Hash(password!),
            RoleId = roleId,
            EmployeeId = employeeId,
            PatientId = patientId,
            IsActive = true,
            UserTheme = Theme.Light
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);

        var dto = await db.Accounts.AsNoTracking().Where(a => a.Id == account.Id).Select(Projections.Accounts).FirstAsync(ct);
        return (null, dto);
    }

    private static async Task<IResult> Create(CreateAccountDto dto, MyDbContext db, CancellationToken ct)
    {
        var (error, account) = await CreateAccountAsync(db, dto.Login, dto.Password, dto.RoleId, dto.EmployeeId, dto.PatientId, ct);
        return error ?? Results.Created($"/api/accounts/{account!.Id}", account);
    }

    // PUT /api/accounts/{id}  { "roleId": 2, "isActive": false } — смена роли и блокировка
    private static async Task<IResult> Update(int id, UpdateAccountDto dto, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (account is null) return Err.NotFound("Аккаунт не найден");

        var roleCode = await db.Roles.Where(r => r.Id == dto.RoleId).Select(r => r.Code).FirstOrDefaultAsync(ct);
        if (roleCode is null) return Err.BadRequest("Роль не найдена");
        if ((roleCode == RoleCode.Patient) != (account.PatientId is not null))
            return Err.BadRequest("Аккаунт пациента может иметь только роль пациента, аккаунт сотрудника — только роль сотрудника");

        // Защита от «запирания»: нельзя заблокировать себя или снять с себя роль
        if (id == user.GetAccountId() && (!dto.IsActive || dto.RoleId != account.RoleId))
            return Err.BadRequest("Нельзя заблокировать свой аккаунт или сменить себе роль");

        account.RoleId = dto.RoleId;
        account.IsActive = dto.IsActive;
        await db.SaveChangesAsync(ct);

        var result = await db.Accounts.AsNoTracking().Where(a => a.Id == id).Select(Projections.Accounts).FirstAsync(ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> ResetPassword(int id, ResetPasswordDto dto, MyDbContext db, CancellationToken ct)
    {
        if (Passwords.Validate(dto.NewPassword) is string error) return Err.BadRequest(error);

        var hash = Passwords.Hash(dto.NewPassword);
        var updated = await db.Accounts.Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PasswordHash, hash), ct);
        return updated == 0 ? Err.NotFound("Аккаунт не найден") : Results.NoContent();
    }

    // Удалить можно только аккаунт без истории в журнале; иначе его блокируют через PUT
    private static async Task<IResult> Delete(int id, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        if (id == user.GetAccountId()) return Err.BadRequest("Нельзя удалить свой аккаунт");
        if (await db.Audits.AnyAsync(a => a.AccountId == id, ct))
            return Err.Conflict("У аккаунта есть записи в журнале аудита. Заблокируйте его вместо удаления");

        var deleted = await db.Accounts.Where(a => a.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Err.NotFound("Аккаунт не найден") : Results.NoContent();
    }

    // ===================== Роли и права =====================
    private static async Task<IResult> Roles(MyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Roles.AsNoTracking()
            .OrderBy(r => r.Id)
            .Select(r => new RoleDto(r.Id, r.Code, r.Name,
                r.PermissionCodes.OrderBy(p => p.Code).Select(p => p.Code).ToList()))
            .ToListAsync(ct));

    private static async Task<IResult> Permissions(MyDbContext db, CancellationToken ct) =>
        Results.Ok(await db.Permissions.AsNoTracking()
            .OrderBy(p => p.Code)
            .Select(p => new PermissionDto(p.Code, p.Name))
            .ToListAsync(ct));

    // PUT /api/roles/{id}/permissions  { "codes": ["patient.view", "patient.manage"] }
    // Полностью заменяет набор прав роли. Вступает в силу при следующем входе пользователей.
    private static async Task<IResult> SetPermissions(int id, RolePermissionsInput dto, ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        var role = await db.Roles.Include(r => r.PermissionCodes).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (role is null) return Err.NotFound("Роль не найдена");

        var codes = (dto.Codes ?? Array.Empty<string>())
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .Distinct()
            .ToList();

        var permissions = await db.Permissions.Where(p => codes.Contains(p.Code)).ToListAsync(ct);
        var unknown = codes.Except(permissions.Select(p => p.Code)).ToList();
        if (unknown.Count > 0) return Err.BadRequest("Неизвестные права: " + string.Join(", ", unknown));

        if (user.IsInRole(role.Code) && !codes.Contains(Perm.RoleManage))
            return Err.BadRequest("Нельзя отнять у своей роли право управления ролями");

        // Меняем только разницу: EF сам удалит и добавит строки в role_permission
        foreach (var p in role.PermissionCodes.Where(p => !codes.Contains(p.Code)).ToList())
            role.PermissionCodes.Remove(p);
        foreach (var p in permissions.Where(p => role.PermissionCodes.All(x => x.Code != p.Code)))
            role.PermissionCodes.Add(p);
        await db.SaveChangesAsync(ct);

        return Results.Ok(new RoleDto(role.Id, role.Code, role.Name, codes.OrderBy(c => c).ToList()));
    }

    // ===================== Журнал аудита =====================

    // GET /api/audits?from=2026-10-01&to=2026-10-06&accountId=&patientId=&action=SELECT&table=visit&page=1
    private static async Task<IResult> Audits(DateOnly? from, DateOnly? to, int? accountId, int? patientId,
        string? action, string? table, int? page, int? pageSize, MyDbContext db, CancellationToken ct)
    {
        if (Rules.ValidateRange(from, to, 366, out var start, out var end) is string error) return Err.BadRequest(error);
        var fromDt = start.ToDateTime(TimeOnly.MinValue);
        var toDt = end.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var query = db.Audits.AsNoTracking().Where(a => a.ActedAt >= fromDt && a.ActedAt < toDt);
        if (accountId is int acc) query = query.Where(a => a.AccountId == acc);
        if (patientId is int p) query = query.Where(a => a.PatientId == p);
        if (Text.Clean(action)?.ToUpperInvariant() is string act) query = query.Where(a => a.Action == act);
        if (Text.Clean(table) is string t) query = query.Where(a => a.TableName == t);

        return Results.Ok(await query
            .OrderByDescending(a => a.ActedAt).ThenByDescending(a => a.Id)
            .Select(Projections.Audits)
            .ToPagedAsync(page, pageSize, ct));
    }
}
