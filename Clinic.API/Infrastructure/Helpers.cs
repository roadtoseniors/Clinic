using System.Text.RegularExpressions;

namespace Clinic.API;

// Единый формат ошибок для клиента: { "status": 400, "title": "текст для пользователя" }
public static class Err
{
    public static IResult BadRequest(string title) => Results.Problem(title: title, statusCode: StatusCodes.Status400BadRequest);
    public static IResult Unauthorized(string title) => Results.Problem(title: title, statusCode: StatusCodes.Status401Unauthorized);
    public static IResult Forbidden(string title = "Недостаточно прав") => Results.Problem(title: title, statusCode: StatusCodes.Status403Forbidden);
    public static IResult NotFound(string title = "Запись не найдена") => Results.Problem(title: title, statusCode: StatusCodes.Status404NotFound);
    public static IResult Conflict(string title) => Results.Problem(title: title, statusCode: StatusCodes.Status409Conflict);
}

// В PostgreSQL даты хранятся как timestamp WITHOUT time zone.
// Npgsql не даёт писать туда DateTime.UtcNow, поэтому везде местное время.
public static class Clock
{
    public static DateTime Now => DateTime.Now;
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}

public static class Text
{
    // Пустая строка или пробелы превращаются в null, остальное обрезается
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Шаблон для ILIKE: экранируем % и _, которые ввёл пользователь
    public static string LikePattern(string value) =>
        "%" + value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    public static string FullName(string lastName, string firstName, string? middleName) =>
        middleName is null ? $"{lastName} {firstName}" : $"{lastName} {firstName} {middleName}";
}

public static class Passwords
{
    public const int MinLength = 8;

    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public static bool Verify(string password, string? hash)
    {
        if (string.IsNullOrEmpty(hash)) return false;
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch
        {
            return false;   // в базе не BCrypt-хеш: считаем пароль неверным, а не роняем запрос
        }
    }

    public static string? Validate(string? password) => string.IsNullOrEmpty(password)
        ? "Введите пароль" : Clinic.Workspace.InputRules.Error("password", password);

}

public static partial class Rules
{
    public static string? ValidateContacts(string? middleName, string? phone, string? snils, string? address) =>
        Clinic.Workspace.InputRules.Error("middle_name", middleName)
        ?? Clinic.Workspace.InputRules.Error("phone", phone)
        ?? Clinic.Workspace.InputRules.Error("snils", snils)
        ?? Clinic.Workspace.InputRules.Error("address", address);

    [GeneratedRegex(@"^[A-Za-z0-9._-]{3,50}$")]
    private static partial Regex LoginRegex();

    public static string? ValidateLogin(string? login) =>
        login is null || !LoginRegex().IsMatch(login.Trim())
            ? "Логин: от 3 до 50 символов, латинские буквы, цифры, точка, дефис или подчёркивание"
            : null;

    // Клиент может прислать латинскую M или строчную букву: приводим к значению из CHECK (М/Ж)
    public static char NormalizeGender(char gender) => gender switch
    {
        'M' or 'm' or 'м' => 'М',
        'F' or 'f' or 'ж' => 'Ж',
        _ => gender
    };

    public static string? ValidatePerson(string? lastName, string? firstName, DateOnly birthDate, char gender)
    {
        if (Text.Clean(lastName) is null || Text.Clean(firstName) is null)
            return "Фамилия и имя обязательны";
        if (Clinic.Workspace.InputRules.Error("last_name", lastName) is string lastError) return lastError;
        if (Clinic.Workspace.InputRules.Error("first_name", firstName) is string firstError) return firstError;
        if (birthDate > Clock.Today || birthDate < new DateOnly(1900, 1, 1))
            return "Неверная дата рождения";
        if (NormalizeGender(gender) is not ('М' or 'Ж'))
            return "Пол: М или Ж";
        return null;
    }

    // Проверка периода для фильтров и отчётов
    public static string? ValidateRange(DateOnly? from, DateOnly? to, int maxDays, out DateOnly start, out DateOnly end)
    {
        end = to ?? Clock.Today;
        start = from ?? end.AddDays(-30);
        if (end < start) return "Дата «по» раньше даты «с»";
        if (end.DayNumber - start.DayNumber > maxDays) return $"Период не больше {maxDays} дней";
        return null;
    }
}

public static class Paging
{
    // Постраничная выдача: page с 1, pageSize от 1 до 200 (по умолчанию 50)
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, int? page, int? pageSize, CancellationToken ct)
    {
        var p = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<T>(items, total, p, size);
    }
}
