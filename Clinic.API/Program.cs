using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Clinic.API;
using Clinic.Context;
using Clinic.Entities;
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

app.UseCors(builder => builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/auth/login", (AuthData data, MyDbContext cnt) =>
{
    Account userAccount = cnt.Accounts.FirstOrDefault(u => u.Login == data.Login && u.PasswordHash == data.Password);
    if (userAccount != null)
    {
        var cliams = new List<Claim> { new Claim(ClaimTypes.Name, data.Login) };

        var jwt = new JwtSecurityToken(
            issuer: AuthOption.ISSUER,
            audience: AuthOption.AUDIENCE,
            claims: cliams,
            expires: DateTime.UtcNow.Add(TimeSpan.FromHours(48)),
            signingCredentials: new SigningCredentials(AuthOption.GetSymmetricSecurityKey(), SecurityAlgorithms.HmacSha256)
            );
        return Results.Ok(new {UserAccount = userAccount, Token = new JwtSecurityTokenHandler().WriteToken(jwt)});
    }
    else
    {
        return Results.Unauthorized();
    }
});

var api = app.MapGroup("/api");
api.RequireAuthorization();

api.MapGet("/roles", async (MyDbContext cnt, CancellationToken ct) =>
{
    TypedResults.Ok(
        await cnt.Roles.AsNoTracking().OrderBy(r => r.Id).Select(r => new RoleDto(r.Id, r.Name)).ToListAsync(ct));
});

api.MapGet("/appointments", async (MyDbContext cnt, CancellationToken ct) =>
{
    TypedResults.Ok(
        await cnt.Appointments.AsNoTracking().OrderByDescending(a => a.Schedule.WorkDate).ThenBy(a => a.Schedule.StartTime)
            .Select(a=> new AppointmentDto(a.Id, a.ScheduleId, a.Schedule.WorkDate, a.StartTime, a.PatientId, a.Status, a.Source, a.CreatedAt))
            .ToListAsync(ct));
});

api.MapGet("/audits", async (MyDbContext cnt, CancellationToken ct) =>
{
    TypedResults.Ok(
        await cnt.Audits.AsNoTracking().OrderByDescending(ad => ad.Id).Take(100).Select(ad => new AuditDto(ad.Id, ad.AccountId, ad.PatientId, ad.ActedAt, ad.Action, ad.TableName, ad.RecordId))
        .ToListAsync(ct));
});

api.MapGet("/diagnoses", async (MyDbContext cnt, CancellationToken ct) =>
{
    TypedResults.Ok(
        await cnt.Diagnoses.AsNoTracking().OrderBy(di => di.Code).Select(di => new DiagnosisDto(di.Code, di.Name)).ToListAsync(ct));
});

api.MapGet("/drugs", async (MyDbContext cnt, CancellationToken ct) =>
{
    TypedResults.Ok(
        await cnt.Drugs.AsNoTracking().OrderBy(dr => dr.Id).Select(dr => new DrugDto(dr.Id, dr.Name)).ToListAsync(ct));
});

api.MapGet("/employees", async (MyDbContext cnt, CancellationToken ct) =>
{
    TypedResults.Ok(
        await cnt.Employees.AsNoTracking().OrderBy(e => e.LastName).ThenBy(e => e.FirstName)
            .Select(e => new EmployeeDto(e.Id, e.FirstName, e.LastName, e.MiddleName, e.PostId, e.Post.Name, e.SpecialtyId,
                e.Specialty != null ? e.Specialty.Name : null, e.Phone, e.IsActive)).ToListAsync(ct));
});

api.MapGet("/invoices", async (MyDbContext cnt, CancellationToken ct) =>
{
    TypedResults.Ok(
        await cnt.Invoices.AsNoTracking().OrderByDescending(i => i.CreatedAt).Select(i => new InvoiceDto(i.Id, i.PatientId, i.CreatedAt, i.Status, i.MethodId, 
            i.PaidAt, i.VisitServices.Sum((vs => vs.Price * vs.Quantity)))).ToListAsync(ct));
});

api.MapGet("/patient", async (MyDbContext cnt, CancellationToken ct) =>
{
    
});