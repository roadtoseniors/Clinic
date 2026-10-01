using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Clinic.API;
using Clinic.Context;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MyDbContext>();
builder.Services.AddCors();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddAuthorization();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = AuthOption.ISSUER,
        ValidateAudience = true,
        ValidAudience = AuthOption.AUDIENCE,
        ValidateLifetime = true,
        IssuerSigningKey = AuthOption.GetSymmetricSecurityKey(),
        ValidateIssuerSigningKey = true,
    };
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
app.UseAuthentication();
app.UseAuthorization();

// ======================= ВХОД =======================
app.MapPost("/auth/login", async (AuthData data, MyDbContext db, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(data.Login) || string.IsNullOrWhiteSpace(data.Password))
        return Results.BadRequest("Логин и пароль обязательны");

    var account = await db.Accounts
        .AsNoTracking()
        .Where(u => u.Login == data.Login && u.IsActive)
        .Select(u => new
        {
            u.Id, u.Login, u.PasswordHash, u.RoleId, RoleCode = u.Role.Code,
            u.EmployeeId, u.PatientId, u.IsActive, u.UserTheme, u.LastLogin
        })
        .FirstOrDefaultAsync(ct);

    if (account is null || !BCrypt.Net.BCrypt.Verify(data.Password, account.PasswordHash))
        return Results.Unauthorized();

    // права роли берём из таблицы role_permission (колонка обязана называться Value)
    var permissions = await db.Database
        .SqlQuery<string>($"SELECT permission_code AS \"Value\" FROM role_permission WHERE role_id = {account.RoleId}")
        .ToListAsync(ct);

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
        expires: DateTime.UtcNow.AddHours(12),
        signingCredentials: new SigningCredentials(
            AuthOption.GetSymmetricSecurityKey(), SecurityAlgorithms.HmacSha256));

    var token = new JwtSecurityTokenHandler().WriteToken(jwt);

    await db.Accounts
        .Where(a => a.Id == account.Id)
        .ExecuteUpdateAsync(s => s.SetProperty(a => a.LastLogin, DateTime.Now), ct);

    var dto = new AccountFullDto(
        account.Id, account.Login, account.RoleId, account.RoleCode,
        account.EmployeeId, account.PatientId, account.IsActive, account.UserTheme, account.LastLogin);

    return Results.Ok(new LoginResponse(dto, token, permissions));
});

// ======================= API (только с токеном) =======================
var api = app.MapGroup("/api");
api.RequireAuthorization();

// ---------- Общие функции ----------
api.MapPut("/me/theme", async (ThemeDto dto, ClaimsPrincipal user, MyDbContext db, CancellationToken ct) =>
{
    if (dto.Theme is not ("light" or "dark"))
        return Results.BadRequest("Тема: light или dark");

    var id = user.GetAccountId();
    if (id is null) return Results.Forbid();

    await db.Accounts
        .Where(a => a.Id == id)
        .ExecuteUpdateAsync(s => s.SetProperty(a => a.UserTheme, dto.Theme), ct);

    return Results.NoContent();
});

// ---------- Справочники (доступны любому вошедшему) ----------
api.MapGet("/paymethods", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.PayMethods.AsNoTracking().OrderBy(m => m.Id)
        .Select(m => new PayMethodDto(m.Id, m.Name, m.IsInsurance)).ToListAsync(ct)));

api.MapGet("/posts", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Posts.AsNoTracking().OrderBy(p => p.Id)
        .Select(p => new PostDto(p.Id, p.Name)).ToListAsync(ct)));

api.MapGet("/specialites", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Specialties.AsNoTracking().OrderBy(s => s.Name)
        .Select(s => new SpecialtyDto(s.Id, s.Name)).ToListAsync(ct)));

api.MapGet("/servicegroups", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.ServiceGroups.AsNoTracking().OrderBy(g => g.Id)
        .Select(g => new ServiceGroupDto(g.Id, g.Name)).ToListAsync(ct)));

api.MapGet("/services", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Services.AsNoTracking().OrderBy(s => s.Name)
        .Select(s => new ServiceDto(s.Id, s.GroupId, s.Name, s.Price, s.IsActive)).ToListAsync(ct)));

api.MapGet("/diagnoses", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Diagnoses.AsNoTracking().OrderBy(d => d.Code)
        .Select(d => new DiagnosisDto(d.Code, d.Name)).ToListAsync(ct)));

api.MapGet("/drugs", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Drugs.AsNoTracking().OrderBy(d => d.Name)
        .Select(d => new DrugDto(d.Id, d.Name)).ToListAsync(ct)));

api.MapGet("/rooms", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Rooms.AsNoTracking().OrderBy(r => r.Number)
        .Select(r => new RoomDto(r.Id, r.Number, r.Floor)).ToListAsync(ct)));

api.MapGet("/insurancecompanies", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.InsuranceCompanies.AsNoTracking().OrderBy(c => c.Name)
        .Select(c => new InsuranceCompanyDto(c.Id, c.Name, c.Phone, c.IsActive)).ToListAsync(ct)));

api.MapGet("/districts", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Districts.AsNoTracking().OrderBy(d => d.Number)
        .Select(d => new DistrictDto(d.Id, d.Number, d.Name)).ToListAsync(ct)));

api.MapGet("/documenttypes", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.DocumentTypes.AsNoTracking().OrderBy(t => t.Id)
        .Select(t => new DocumentTypeDto(t.Id, t.Name)).ToListAsync(ct)));

api.MapGet("/employees", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Employees.AsNoTracking()
        .OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
        .Select(e => new EmployeeDto(
            e.Id, e.LastName, e.FirstName, e.MiddleName,
            e.PostId, e.Post.Name,
            e.SpecialtyId, e.Specialty != null ? e.Specialty.Name : null,
            e.Phone, e.IsActive))
        .ToListAsync(ct)));

api.MapGet("/schedules", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Schedules.AsNoTracking()
        .OrderByDescending(s => s.WorkDate).ThenBy(s => s.StartTime)
        .Select(s => new ScheduleDto(s.Id, s.EmployeeId, s.RoomId, s.WorkDate, s.StartTime, s.EndTime, s.SlotMinutes))
        .ToListAsync(ct)));

// ---------- Данные с ограничением по правам ----------
api.MapGet("/roles", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Roles.AsNoTracking().OrderBy(r => r.Id)
        .Select(r => new RoleDto(r.Id, r.Code, r.Name)).ToListAsync(ct)))
    .RequirePermission(Perm.RoleManage);

api.MapGet("/patient", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Patients.AsNoTracking()
        .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
        .Select(p => new PatientDto(
            p.Id, p.CardNumber, p.LastName, p.FirstName, p.MiddleName, p.BirthDate, p.Gender,
            p.Snils, p.Address, p.Phone, p.CreatedDate, p.IsActive))
        .ToListAsync(ct)))
    .RequirePermission(Perm.PatientView);

api.MapGet("/patient/{id:int}/policies", async (int id, MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.PatientPolicies.AsNoTracking()
        .Where(p => p.PatientId == id)
        .OrderBy(p => p.Kind).ThenByDescending(p => p.IsActive)
        .Select(p => new PolicyDto(
            p.Id, p.PatientId, p.Kind, p.Number, p.CompanyId, p.Company.Name,
            p.ValidFrom, p.ValidTo, p.IsActive))
        .ToListAsync(ct)))
    .RequirePermission(Perm.PatientView);

api.MapGet("/appointments", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Appointments.AsNoTracking()
        .OrderByDescending(a => a.Schedule.WorkDate).ThenBy(a => a.StartTime)
        .Select(a => new AppointmentDto(
            a.Id, a.ScheduleId, a.Schedule.WorkDate, a.StartTime,
            a.PatientId, a.Status, a.Source, a.CreatedAt))
        .ToListAsync(ct)))
    .RequirePermission(Perm.AppointmentManage);

api.MapGet("/visits", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Visits.AsNoTracking().OrderByDescending(v => v.VisitedAt)
        .Select(v => new VisitDto(
            v.Id, v.AppointmentId, v.VisitedAt, v.Complaints, v.Anamnesis,
            v.ObjectiveStatus, v.DiagnosisCode, v.Recommendations, v.IsClosed))
        .ToListAsync(ct)))
    .RequirePermission(Perm.MedcardView);

api.MapGet("/prescriptions", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Prescriptions.AsNoTracking().OrderBy(p => p.Id)
        .Select(p => new PrescriptionDto(p.Id, p.VisitId, p.DrugId, p.Dosage, p.Instruction, p.IsRecipe))
        .ToListAsync(ct)))
    .RequirePermission(Perm.MedcardView);

api.MapGet("/referrals", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Referrals.AsNoTracking().OrderBy(r => r.Id)
        .Select(r => new ReferralDto(r.Id, r.VisitId, r.ServiceId, r.Status, r.ExecutorId, r.DoneAt, r.Result))
        .ToListAsync(ct)))
    .RequirePermission(Perm.ReferralView);

api.MapGet("/visitservices", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.VisitServices.AsNoTracking().OrderByDescending(vs => vs.RenderedAt)
        .Select(vs => new VisitServiceDto(
            vs.Id, vs.VisitId, vs.ServiceId, vs.InvoiceId, vs.Quantity, vs.Price, vs.RenderedAt))
        .ToListAsync(ct)))
    .RequirePermission(Perm.ServiceRender, Perm.InvoiceManage);

api.MapGet("/invoices", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Invoices.AsNoTracking().OrderByDescending(i => i.CreatedAt)
        .Select(i => new InvoiceDto(
            i.Id, i.PatientId, i.CreatedAt, i.Status, i.MethodId, i.PolicyId, i.PaidAt,
            i.VisitServices.Sum(vs => vs.Price * vs.Quantity)))
        .ToListAsync(ct)))
    .RequirePermission(Perm.InvoiceManage);

api.MapGet("/audits", async (MyDbContext db, CancellationToken ct) =>
    TypedResults.Ok(await db.Audits.AsNoTracking().OrderByDescending(a => a.ActedAt)
        .Take(100)
        .Select(a => new AuditDto(a.Id, a.AccountId, a.PatientId, a.ActedAt, a.Action, a.TableName, a.RecordId))
        .ToListAsync(ct)))
    .RequirePermission(Perm.AuditView);

// ---------- Кабинет пациента: только собственные данные (id берётся из токена) ----------
api.MapGet("/my/appointments", async (ClaimsPrincipal user, MyDbContext db, CancellationToken ct) =>
{
    if (user.GetPatientId() is not int patientId)
        return Results.Forbid();

    var list = await db.Appointments.AsNoTracking()
        .Where(a => a.PatientId == patientId)
        .OrderByDescending(a => a.Schedule.WorkDate).ThenBy(a => a.StartTime)
        .Select(a => new MyAppointmentDto(
            a.Id, a.Schedule.WorkDate, a.StartTime, a.Status,
            a.Schedule.Employee.LastName + " " + a.Schedule.Employee.FirstName,
            a.Schedule.Employee.Specialty != null ? a.Schedule.Employee.Specialty.Name : null,
            a.Schedule.Room.Number))
        .ToListAsync(ct);

    return Results.Ok(list);
})
.RequirePermission(Perm.OwnView);

api.MapGet("/my/notifications", async (ClaimsPrincipal user, MyDbContext db, CancellationToken ct) =>
{
    if (user.GetPatientId() is not int patientId)
        return Results.Forbid();

    var list = await db.Notifications.AsNoTracking()
        .Where(n => n.PatientId == patientId)
        .OrderBy(n => n.IsRead).ThenByDescending(n => n.CreatedAt)   // непрочитанные первыми
        .Take(50)
        .Select(n => new NotificationDto(n.Id, n.CreatedAt, n.Message, n.IsRead))
        .ToListAsync(ct);

    return Results.Ok(list);
})
.RequirePermission(Perm.OwnView);

api.MapPost("/my/notifications/{id:int}/read", async (int id, ClaimsPrincipal user, MyDbContext db, CancellationToken ct) =>
{
    if (user.GetPatientId() is not int patientId)
        return Results.Forbid();

    var updated = await db.Notifications
        .Where(n => n.Id == id && n.PatientId == patientId)
        .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);

    return updated == 0 ? Results.NotFound() : Results.NoContent();
})
.RequirePermission(Perm.OwnView);

app.Run();