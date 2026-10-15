using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Clinic.Workspace;

namespace Clinic.Data;

public sealed partial class HttpClinicGateway : IClinicGateway, IDisposable
{
    private readonly HttpClient _http;
    public event Action<string>? SessionExpired;
    private ClinicSession? _session;
    private string? _captchaId, _captchaAnswer;
    private string? _login;
    public void SetCaptchaResponse(string? id, string? answer)
    {
        _captchaId = id;
        _captchaAnswer = answer;
    }

    public async Task<CaptchaChallenge> RefreshCaptchaAsync(string login) => ParseChallenge(await Request("POST", "auth/captcha", new { login }));
    private static CaptchaChallenge ParseChallenge(JsonElement value) => new(Text(value, "challengeId"), Text(value, "imageBase64"), DateTimeOffset.Parse(Text(value, "expiresAt"), CultureInfo.InvariantCulture));
    public async Task SignOutAsync() => _ = await Request("POST", "api/me/logout");
    public IReadOnlyList<string> Permissions { get; private set; } = [];

    public HttpClinicGateway(string address, HttpClient? client = null)
    {
        if (!Uri.TryCreate(address.TrimEnd('/') + "/", UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
            throw new InvalidOperationException("Некорректные настройки подключения. Обратитесь к администратору.");
        _http = client ?? new HttpClient();
        _http.BaseAddress = uri;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public void Dispose() => _http.Dispose();
    private static JsonElement Property(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var result) ? result : default;
    private static string Text(JsonElement value, string key) => Property(value, key).ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? "" : Property(value, key).ToString();
    private static int Integer(JsonElement value, string key) => int.TryParse(Text(value, key), out var id) ? id : 0;
    private static bool Boolean(JsonElement value, string key) => Text(value, key) == "True" || Text(value, key) == "true";
    private static List<JsonElement> Items(JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(x => x.Clone()).ToList() : [];
    private static string FullName(JsonElement v) => string.Join(' ', new[] { Text(v, "lastName"), Text(v, "firstName"), Text(v, "middleName") }.Where(x => x.Length > 0));
    private static string DisplayDate(string value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, out var d) ? d.ToString("dd.MM.yyyy") : value;
    private static string Time(JsonElement value, string key) => Text(value, key).Length >= 5 ? Text(value, key)[..5] : Text(value, key);
    private static string Money(JsonElement value, string key)
    {
        if (!decimal.TryParse(Text(value, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            return "—";
        return amount.ToString(amount == decimal.Truncate(amount) ? "N0" : "N2", CultureInfo.GetCultureInfo("ru-RU")) + " ₽";
    }

    private static string Snake(string key) => string.Concat(key.Select((c, i) => char.IsUpper(c) && i > 0 ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    private static Record Row(JsonElement json, string c1, string c2, string c3, string c4, string status, string details = "", Dictionary<string, string>? extra = null)
    {
        var values = json.ValueKind == JsonValueKind.Object ? json.EnumerateObject().Where(p => p.Value.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object)).ToDictionary(p => Snake(p.Name), p => p.Value.ValueKind == JsonValueKind.Null ? "" : p.Value.ToString()) : new Dictionary<string, string>();
        foreach (var key in values.Keys.ToArray())
            if (values[key] is "True" or "False")
                values[key] = values[key].ToLowerInvariant();
        if (extra is not null)
            foreach (var(key, value)in extra)
                values[key] = value;
        return new()
        {
            Id = values.TryGetValue("id", out var recordId) && int.TryParse(recordId, out var number) ? number : Integer(json, "id"),
            Cell1 = c1,
            Cell2 = c2,
            Cell3 = c3,
            Cell4 = c4,
            Status = status,
            Details = details,
            Values = values
        };
    }

    private void Check(ClinicSession s)
    {
        if (_session is null || s.AccountId != _session.AccountId || s.Role != _session.Role || s.PatientId != _session.PatientId || s.EmployeeId != _session.EmployeeId)
            throw new InvalidOperationException("Сессия завершена. Войдите снова.");
    }

    private async Task<JsonElement> Request(string method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
            request.Content = body is HttpContent content ? content : JsonContent.Create(body);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Не удалось подключиться к поликлинике. Проверьте сеть и повторите попытку.");
        }
        catch (TaskCanceledException)
        {
            throw new InvalidOperationException("Сервис не ответил вовремя. Повторите попытку.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode == HttpStatusCode.Unauthorized ? "Сессия истекла или неверный пароль. Войдите снова." : "Не удалось выполнить действие. Повторите попытку.";
                try
                {
                    using var document = JsonDocument.Parse(text);
                    var error = document.RootElement;
                    var title = Text(error, "title");
                    if (title.Length > 0)
                        message = title;
                    if (path == "auth/login" && Property(error, "captcha").ValueKind == JsonValueKind.Object)
                        throw new CaptchaRequiredException(message, ParseChallenge(Property(error, "captcha")));
                }
                catch (JsonException)
                {
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && !path.StartsWith("auth/", StringComparison.Ordinal))
                {
                    SessionExpired?.Invoke("Сессия завершена. Войдите снова.");
                    throw new SessionExpiredException("Сессия завершена. Войдите снова.");
                }

                throw new InvalidOperationException(message);
            }

            if (text.Length == 0)
                return default;
            using var payload = JsonDocument.Parse(text);
            return payload.RootElement.Clone();
        }
    }

    private async Task<List<JsonElement>> Many(string path)
    {
        var rows = new List<JsonElement>();
        var page = 1;
        while (true)
        {
            var json = await Request("GET", path + (path.Contains('?') ? "&" : "?") + "page=" + page + "&pageSize=200");
            if (json.ValueKind == JsonValueKind.Array)
                return Items(json);
            var items = Items(Property(json, "items"));
            rows.AddRange(items);
            if (items.Count == 0 || rows.Count >= Integer(json, "total"))
                return rows;
            if (++page > 1000)
                throw new InvalidOperationException("Слишком большой список. Уточните фильтр.");
        }
    }

    public async Task<(ClinicSession Session, LiveDashboard Dashboard)?> SignInAsync(string login, string password)
    {
        _session = null;
        _http.DefaultRequestHeaders.Authorization = null;
        var result = await Request("POST", "auth/login", new { login, password, challengeId = _captchaId, captchaAnswer = _captchaAnswer });
        var account = Property(result, "userAccount");
        var role = Text(account, "roleCode") switch
        {
            "patient" => "Пациент",
            "registrar" => "Регистратор",
            "doctor" => "Врач",
            "admin" or "administrator" => "Администратор",
            _ => Text(account, "roleName")};
        if (role is not ("Пациент" or "Регистратор" or "Врач" or "Администратор"))
            throw new InvalidOperationException("Эта роль не входит в приложение.");
        _login = login;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Text(result, "token"));
        Permissions = Items(Property(result, "permissions")).Select(x => x.ToString()).ToArray();
        _session = new(Integer(account, "id"), Integer(account, "employeeId") == 0 ? null : Integer(account, "employeeId"), Integer(account, "patientId") == 0 ? null : Integer(account, "patientId"), Text(account, "login"), role);
        if (role != "Пациент" || Permissions.Contains("own.view"))
        {
            var person = role == "Пациент" ? Property(await Request("GET", "api/my/profile"), "patient") : await Request("GET", "api/employees/" + _session.EmployeeId);
            _session = _session with
            {
                Name = FullName(person)
            };
        }

        return (_session, await DashboardAsync(_session));
    }

    public async Task<bool?> GetUserThemeAsync(int accountId)
    {
        if (_session?.AccountId != accountId)
            throw new InvalidOperationException("Нет активной сессии.");
        return Text(Property(await Request("GET", "api/me"), "account"), "userTheme") == "dark";
    }

    public async Task SaveUserThemeAsync(int accountId, bool dark)
    {
        if (_session?.AccountId != accountId)
            throw new InvalidOperationException("Нет активной сессии.");
        await Request("PUT", "api/me/theme", new { theme = dark ? "dark" : "light" });
    }

    public async Task ChangePasswordAsync(string oldPassword, string newPassword)
    {
        var login = _login ?? throw new InvalidOperationException("Войдите снова.");
        await Request("PUT", "api/me/password", new { oldPassword, newPassword });
        await SignInAsync(login, newPassword);
    }
}
