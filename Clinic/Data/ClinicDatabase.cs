using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Clinic.Demo;
using Npgsql;

namespace Clinic.Data;

public sealed record ClinicSession(int AccountId, int? EmployeeId, int? PatientId, string Name, string Role);
public sealed record LiveDashboard(DashboardMetric[] Metrics, DashboardItem[] Items, string SectionTitle, string Note);

// Temporary Desktop-only adapter for the isolated course database. The shared API can replace it later.
public sealed class ClinicDatabase
{
    private readonly string _connectionString;

    private ClinicDatabase(string connectionString) => _connectionString = connectionString;

    public static ClinicDatabase FromLocalConfiguration()
    {
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("Браузерный клиент не подключается к PostgreSQL напрямую.");

        var connectionString = Environment.GetEnvironmentVariable("CLINIC_DB_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            var path = Path.Combine(AppContext.BaseDirectory, "db.local.json");
            if (!File.Exists(path))
                throw new InvalidOperationException("Не найден db.local.json. Настройте локальную тестовую БД.");
            using var config = JsonDocument.Parse(File.ReadAllText(path));
            connectionString = config.RootElement.GetProperty("ConnectionString").GetString();
        }
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Строка подключения к тестовой БД пуста.");
        return new ClinicDatabase(connectionString);
    }

    public async Task<(ClinicSession Session, LiveDashboard Dashboard)?> SignInAsync(string login, string password)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT a.id, r.name,
                   COALESCE(e.last_name || ' ' || e.first_name || COALESCE(' ' || e.middle_name, ''),
                            p.last_name || ' ' || p.first_name || COALESCE(' ' || p.middle_name, '')) AS full_name,
                   a.employee_id, a.patient_id
            FROM account a
            JOIN role r ON r.id = a.role_id
            LEFT JOIN employee e ON e.id = a.employee_id
            LEFT JOIN patient p ON p.id = a.patient_id
            WHERE lower(a.login) = lower(@login)
              AND a.is_active
              AND a.password_hash = crypt(@password, a.password_hash)
              AND (e.id IS NULL OR e.is_active)
              AND (p.id IS NULL OR p.is_active)
            """, connection);
        command.Parameters.AddWithValue("login", login.Trim());
        command.Parameters.AddWithValue("password", password);
        ClinicSession session;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync()) return null;
            session = new ClinicSession(reader.GetInt32(0), reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.GetString(2), reader.GetString(1));
        }
        return (session, await LoadDashboardAsync(connection, session));
    }

    public async Task<bool?> GetUserThemeAsync(int accountId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT theme FROM user_theme WHERE account_id = @accountId", connection);
        command.Parameters.AddWithValue("accountId", accountId);
        var result = await command.ExecuteScalarAsync();
        return result is string theme ? theme == "dark" : null;
    }

    public async Task SaveUserThemeAsync(int accountId, bool isDark)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO user_theme (account_id, theme)
            VALUES (@accountId, @theme)
            ON CONFLICT (account_id) DO UPDATE
            SET theme = EXCLUDED.theme, updated_at = now()
            """, connection);
        command.Parameters.AddWithValue("accountId", accountId);
        command.Parameters.AddWithValue("theme", isDark ? "dark" : "light");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<LiveDashboard> LoadDashboardAsync(NpgsqlConnection connection, ClinicSession user)
    {
        var id = user.PatientId ?? user.EmployeeId ?? user.AccountId;
        switch (user.Role)
        {
            case "Пациент":
                return new LiveDashboard(
                [Metric(await Count(connection, "SELECT count(*) FROM appointment WHERE patient_id=@id AND status='запланирована'", id), "Будущие записи", "Ваши приёмы", "#E4F2FF"),
                 Metric(await Count(connection, "SELECT count(*) FROM referral r JOIN visit v ON v.id=r.visit_id JOIN appointment a ON a.id=v.appointment_id WHERE a.patient_id=@id AND r.status='выполнено'", id), "Результаты", "Готовые исследования", "#E1F6ED"),
                 Metric(await Count(connection, "SELECT count(*) FROM visit v JOIN appointment a ON a.id=v.appointment_id WHERE a.patient_id=@id", id), "Посещения", "В истории", "#FFF2DF")],
                await Items(connection, """
                    SELECT to_char(s.work_date, 'DD.MM') || ' ' || to_char(a.start_time, 'HH24:MI'),
                           'Приём: ' || e.last_name || ' ' || e.first_name,
                           COALESCE(sp.name, 'Специалист') || ' · кабинет ' || rm.number, a.status
                    FROM appointment a JOIN schedule s ON s.id=a.schedule_id
                    JOIN employee e ON e.id=s.employee_id LEFT JOIN specialty sp ON sp.id=e.specialty_id
                    JOIN room rm ON rm.id=s.room_id WHERE a.patient_id=@id
                    ORDER BY s.work_date DESC, a.start_time DESC LIMIT 5
                    """, id), "Мои записи", "Показаны только записи вошедшего пациента.");
            case "Регистратор":
                return new LiveDashboard(
                [Metric(await Count(connection, "SELECT count(*) FROM appointment a JOIN schedule s ON s.id=a.schedule_id WHERE s.work_date=CURRENT_DATE AND a.status='запланирована'", id), "Записи сегодня", "По всем врачам", "#E4F2FF"),
                 Metric(await Count(connection, "SELECT count(*) FROM patient WHERE is_active", id), "Пациенты", "Активные карты", "#E1F6ED"),
                 Metric(await Count(connection, "SELECT count(*) FROM invoice WHERE status='не оплачен'", id), "Неоплаченные счета", "Нужны действия", "#FFF2DF")],
                await Items(connection, """
                    SELECT to_char(s.work_date, 'DD.MM') || ' ' || to_char(a.start_time, 'HH24:MI'),
                           p.last_name || ' ' || p.first_name || COALESCE(' ' || p.middle_name, ''),
                           e.last_name || ' ' || e.first_name || ' · кабинет ' || rm.number, a.status
                    FROM appointment a JOIN schedule s ON s.id=a.schedule_id JOIN patient p ON p.id=a.patient_id
                    JOIN employee e ON e.id=s.employee_id JOIN room rm ON rm.id=s.room_id
                    WHERE s.work_date >= CURRENT_DATE ORDER BY s.work_date, a.start_time LIMIT 5
                    """, id), "Ближайшие записи", "Список составлен из расписания и записей тестовой БД.");
            case "Врач":
                return new LiveDashboard(
                [Metric(await Count(connection, "SELECT count(*) FROM appointment a JOIN schedule s ON s.id=a.schedule_id WHERE s.employee_id=@id AND s.work_date=CURRENT_DATE", id), "Приёмы сегодня", "Ваше расписание", "#E4F2FF"),
                 Metric(await Count(connection, "SELECT count(*) FROM appointment a JOIN schedule s ON s.id=a.schedule_id WHERE s.employee_id=@id AND s.work_date>=CURRENT_DATE AND a.status='запланирована'", id), "Предстоящие", "Запланированы", "#E1F6ED"),
                 Metric(await Count(connection, "SELECT count(*) FROM referral r JOIN visit v ON v.id=r.visit_id JOIN appointment a ON a.id=v.appointment_id JOIN schedule s ON s.id=a.schedule_id WHERE s.employee_id=@id AND r.status='выдано'", id), "Направления", "Ожидают результата", "#FFF2DF")],
                await Items(connection, """
                    SELECT to_char(s.work_date, 'DD.MM') || ' ' || to_char(a.start_time, 'HH24:MI'),
                           p.last_name || ' ' || p.first_name || COALESCE(' ' || p.middle_name, ''),
                           'Медкарта ' || p.card_number || ' · кабинет ' || rm.number, a.status
                    FROM appointment a JOIN schedule s ON s.id=a.schedule_id JOIN patient p ON p.id=a.patient_id
                    JOIN room rm ON rm.id=s.room_id WHERE s.employee_id=@id AND s.work_date>=CURRENT_DATE
                    ORDER BY s.work_date, a.start_time LIMIT 5
                    """, id), "Мои пациенты", "Показаны пациенты, записанные к вошедшему врачу.");
            case "Лаборант":
                return new LiveDashboard(
                [Metric(await Count(connection, "SELECT count(*) FROM referral WHERE status='выдано'", id), "В очереди", "Невыполненные направления", "#FFF2DF"),
                 Metric(await Count(connection, "SELECT count(*) FROM referral WHERE executor_id=@id AND status='выполнено'", id), "Выполнено", "Ваши результаты", "#E1F6ED"),
                 Metric(await Count(connection, "SELECT count(*) FROM service s JOIN service_group g ON g.id=s.group_id WHERE g.name='Анализы' AND s.is_active", id), "Виды анализов", "Доступные услуги", "#E4F2FF")],
                await Items(connection, """
                    SELECT p.card_number, sv.name,
                           p.last_name || ' ' || p.first_name, r.status
                    FROM referral r JOIN service sv ON sv.id=r.service_id JOIN visit v ON v.id=r.visit_id
                    JOIN appointment a ON a.id=v.appointment_id JOIN patient p ON p.id=a.patient_id
                    WHERE r.status='выдано' ORDER BY r.id LIMIT 5
                    """, id), "Очередь исследований", "Результаты и направления берутся из тестовой БД.");
            default:
                return new LiveDashboard(
                [Metric(await Count(connection, "SELECT count(*) FROM account WHERE is_active", id), "Активные учётные записи", "Все роли", "#E4F2FF"),
                 Metric(await Count(connection, "SELECT count(*) FROM role", id), "Роли", "В справочнике", "#E1F6ED"),
                 Metric(await Count(connection, "SELECT count(*) FROM audit", id), "События аудита", "В журнале", "#FFF2DF")],
                await Items(connection, """
                    SELECT to_char(acted_at, 'DD.MM HH24:MI'), action || ' · ' || table_name,
                           'Учётная запись №' || account_id::text, 'Аудит'
                    FROM audit ORDER BY acted_at DESC LIMIT 5
                    """, id), "Последние события", "Журнал аудита тестовой БД; содержимое медицинских карт здесь не показывается.");
        }
    }

    private static DashboardMetric Metric(long value, string label, string detail, string accent)
        => new(value.ToString(), label, detail, accent);

    private static async Task<long> Count(NpgsqlConnection connection, string sql, int id)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task<DashboardItem[]> Items(NpgsqlConnection connection, string sql, int id)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        var items = new System.Collections.Generic.List<DashboardItem>();
        while (await reader.ReadAsync())
            items.Add(new DashboardItem(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return items.ToArray();
    }
}
