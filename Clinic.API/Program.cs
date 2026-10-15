using Clinic.API;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("api.local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables();
var connection = builder.Configuration.GetConnectionString("Clinic")
    ?? Environment.GetEnvironmentVariable("CLINIC_DB_CONNECTION")
    ?? throw new InvalidOperationException("Укажите ConnectionStrings:Clinic в api.local.json или CLINIC_DB_CONNECTION.");
AuthOption.SigningKey = builder.Configuration["Auth:SigningKey"] ?? Environment.GetEnvironmentVariable("CLINIC_JWT_KEY") ?? "";
_ = AuthOption.GetSymmetricSecurityKey();
builder.Services.AddDbContext<MyDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddCors();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LoginProtection>();

// Глобальная обработка ошибок: try/catch в эндпоинтах не нужен
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
// Кривой JSON или неверный параметр превращаются в исключение, и обработчик выше отдаёт понятный 400
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
// Время приёма можно присылать как "10:30" (стандартный JSON требует "10:30:00")
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new TimeOnlyJsonConverter()));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddHostedService<ParentProcessLifetime>();

builder.Services.AddAuthorization();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.Events = new JwtBearerEvents { OnTokenValidated = SessionTokens.ValidateAsync };
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = AuthOption.ISSUER,
        ValidateAudience = true,
        ValidAudience = AuthOption.AUDIENCE,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1),
        IssuerSigningKey = AuthOption.GetSymmetricSecurityKey(),
        ValidateIssuerSigningKey = true,
    };
});

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MyDbContext>();
    await RuntimeSchema.EnsureAsync(db);
}

app.MapGet("/health", async (MyDbContext db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok", database = db.Database.GetDbConnection().Database }) : Results.StatusCode(503));

app.UseExceptionHandler();   // самым первым в pipeline
app.UseCors(policy => policy
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader()
    .WithExposedHeaders("Content-Disposition"));   // чтобы клиент видел имя скачиваемого файла
app.UseAuthentication();
app.UseAuthorization();

// /auth/login и /auth/register открыты: без них нельзя получить токен
app.MapAuthEndpoints();

// Всё остальное только с токеном. Конкретные права проверяются в каждом эндпоинте (RequirePermission).
var api = app.MapGroup("/api").RequireAuthorization();
api.AddEndpointFilter<MedicalAccessFilter>();
api.AddEndpointFilter<ChangeAuditFilter>();

api.MapClientEndpoints();
api.MapMeEndpoints();          // профиль, тема, пароль, кабинет пациента /api/my/...
api.MapReferenceEndpoints();   // справочники
api.MapEmployeeEndpoints();    // сотрудники и список врачей
api.MapPatientEndpoints();     // пациенты, полисы, прикрепление, документы
api.MapScheduleEndpoints();    // расписание и свободные слоты
api.MapAppointmentEndpoints(); // запись, перенос, отмена, неявка
api.MapVisitEndpoints();       // приём врача, медкарта, назначения, направления
api.MapReferralEndpoints();    // очередь исследований, результаты, файлы
api.MapBillingEndpoints();     // оказанные услуги и счета
api.MapAdminEndpoints();       // аккаунты, роли и права, журнал аудита
api.MapReportEndpoints();      // отчёты
api.MapBackupEndpoints();      // резервные копии

app.Run();
