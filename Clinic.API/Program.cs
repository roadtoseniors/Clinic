using Clinic.API;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MyDbContext>();
builder.Services.AddCors();

// Глобальная обработка ошибок: try/catch в эндпоинтах не нужен
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
// Кривой JSON или неверный параметр превращаются в исключение, и обработчик выше отдаёт понятный 400
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
// Время приёма можно присылать как "10:30" (стандартный JSON требует "10:30:00")
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new TimeOnlyJsonConverter()));

builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<BackupService>();

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
        ClockSkew = TimeSpan.FromMinutes(1),
        IssuerSigningKey = AuthOption.GetSymmetricSecurityKey(),
        ValidateIssuerSigningKey = true,
    };
});

var app = builder.Build();

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