using System.IdentityModel.Tokens.Jwt;
using Clinic.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Clinic.API;

// Открытые эндпоинты: вход и самостоятельная регистрация пациента
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth");
        auth.MapPost("/login", Login);
        auth.MapGet("/insurancecompanies", async (MyDbContext db,CancellationToken ct) => Results.Ok(await db.InsuranceCompanies.AsNoTracking().Where(c=>c.IsActive).OrderBy(c=>c.Name).Select(c=>new NamedDto(c.Id,c.Name)).ToListAsync(ct)));
        auth.MapPost("/register", Register);
    }

    // POST /auth/login
    private static async Task<IResult> Login(AuthData data, MyDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(data.Login) || string.IsNullOrWhiteSpace(data.Password))
            return Err.BadRequest("Логин и пароль обязательны");

        var login = data.Login.Trim();
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Login == login)
            .Select(a => new { a.Id, a.PasswordHash, a.IsActive })
            .FirstOrDefaultAsync(ct);

        // Одинаковый ответ для «нет такого логина» и «неверный пароль»: так нельзя подбирать логины
        if (account is null || !Passwords.Verify(data.Password, account.PasswordHash))
            return Err.Unauthorized("Неверный логин или пароль");

        if (!account.IsActive)
            return Err.Forbidden("Аккаунт заблокирован. Обратитесь в регистратуру");

        return Results.Ok(await TokenIssuer.IssueAsync(db, account.Id, ct));
    }

    // POST /auth/register — пациент регистрируется сам: пациент + полис ОМС + аккаунт одной транзакцией
    private static async Task<IResult> Register(RegisterPatientDto dto, MyDbContext db, CancellationToken ct)
    {
        var error = Rules.ValidateLogin(dto.Login)
                    ?? Passwords.Validate(dto.Password)
                    ?? Rules.ValidatePerson(dto.LastName, dto.FirstName, dto.BirthDate, dto.Gender)
                    ?? (Text.Clean(dto.OmsNumber) is null ? "Укажите номер полиса ОМС" : null);
        if (error is not null)
            return Err.BadRequest(error);

        var login = dto.Login.Trim();
        var omsNumber = dto.OmsNumber.Trim();
        var snils = Text.Clean(dto.Snils);

        if (await db.Accounts.AnyAsync(a => a.Login == login, ct))
            return Err.Conflict("Такой логин уже занят");
        if (snils is not null && await db.Patients.AnyAsync(p => p.Snils == snils, ct))
            return Err.Conflict("Пациент с таким СНИЛС уже зарегистрирован. Обратитесь в регистратуру");
        if (await db.PatientPolicies.AnyAsync(p => p.Number == omsNumber, ct))
            return Err.Conflict("Полис с таким номером уже зарегистрирован. Обратитесь в регистратуру");
        if (!await db.InsuranceCompanies.AnyAsync(c => c.Id == dto.OmsCompanyId && c.IsActive, ct))
            return Err.BadRequest("Страховая компания не найдена");

        var roleId = await db.Roles
            .Where(r => r.Code == RoleCode.Patient)
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (roleId is null)
            return Results.Problem(title: "В базе нет роли пациента (role.code = 'patient')", statusCode: 500);

        // card_number не задаём: его генерирует база
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

        var policy = new PatientPolicy
        {
            Patient = patient,
            Kind = PolicyKind.Oms,
            Number = omsNumber,
            CompanyId = dto.OmsCompanyId,
            ValidFrom = Clock.Today,
            IsActive = true
        };

        var account = new Account
        {
            Login = login,
            PasswordHash = Passwords.Hash(dto.Password),
            RoleId = roleId.Value,
            Patient = patient,
            IsActive = true,
            UserTheme = Theme.Light
        };

        db.Patients.Add(patient);
        db.PatientPolicies.Add(policy);
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);   // один SaveChanges = одна транзакция

        // Сразу выдаём токен, чтобы после регистрации не нужно было входить заново
        return Results.Ok(await TokenIssuer.IssueAsync(db, account.Id, ct));
    }
}

public static class TokenIssuer
{
    public static async Task<LoginResponse> IssueAsync(MyDbContext db, int accountId, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(Projections.AccountsFull)
            .FirstAsync(ct);

        var permissions = await PermissionsOfRoleAsync(db, account.RoleId, ct);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new(ClaimTypes.Name, account.Login),
            new(ClaimTypes.Role, account.RoleCode)
        };
        if (account.PatientId is int patientId)
            claims.Add(new Claim("patientId", patientId.ToString()));
        if (account.EmployeeId is int employeeId)
            claims.Add(new Claim("employeeId", employeeId.ToString()));
        claims.AddRange(permissions.Select(code => new Claim(Perm.ClaimType, code)));

        var jwt = new JwtSecurityToken(
            issuer: AuthOption.ISSUER,
            audience: AuthOption.AUDIENCE,
            claims: claims,
            expires: DateTime.UtcNow.Add(AuthOption.TokenLifetime),   // для JWT нужен именно UtcNow
            signingCredentials: new SigningCredentials(
                AuthOption.GetSymmetricSecurityKey(), SecurityAlgorithms.HmacSha256));

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);

        await db.Accounts
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastLogin, Clock.Now), ct);

        return new LoginResponse(account, token, permissions);
    }

    // Права роли из таблицы role_permission (для SqlQuery<string> колонка обязана называться Value)
    public static Task<List<string>> PermissionsOfRoleAsync(MyDbContext db, int roleId, CancellationToken ct) =>
        db.Database
            .SqlQuery<string>($"SELECT permission_code AS \"Value\" FROM role_permission WHERE role_id = {roleId} ORDER BY permission_code")
            .ToListAsync(ct);
}
