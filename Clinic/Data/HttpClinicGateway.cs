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
    private string? _captchaId;
    private string? _captchaAnswer;
    private string? _login;
    public void SetCaptchaResponse(string? id, string? answer)
    {
        _captchaId = id;
        _captchaAnswer = answer;
    }

    public async Task<CaptchaChallenge> RefreshCaptchaAsync(string login)
    {
        var response = await Request("POST", "auth/captcha", new { login });
        return ParseChallenge(response);
    }

    private static CaptchaChallenge ParseChallenge(JsonElement value)
    {
        var expiresAt = DateTimeOffset.Parse(Text(value, "expiresAt"), CultureInfo.InvariantCulture);
        return new CaptchaChallenge(Text(value, "challengeId"), Text(value, "imageBase64"), expiresAt);
    }

    public async Task SignOutAsync()
    {
        await Request("POST", "api/me/logout");
    }
    public IReadOnlyList<string> Permissions { get; private set; } = [];

    public HttpClinicGateway(string address, HttpClient? client = null)
    {
        if (!Uri.TryCreate(address.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0)
        {
            throw new InvalidOperationException("Некорректные настройки подключения. Обратитесь к администратору.");
        }
        _http = client ?? new HttpClient();
        _http.BaseAddress = uri;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private static JsonElement Property(JsonElement value, string key)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var property))
        {
            return property;
        }

        return default;
    }

    private static string Text(JsonElement value, string key)
    {
        var property = Property(value, key);
        if (property.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "";
        }

        return property.ToString();
    }

    private static int Integer(JsonElement value, string key)
    {
        if (int.TryParse(Text(value, key), out var number))
        {
            return number;
        }

        return 0;
    }

    private static bool Boolean(JsonElement value, string key)
    {
        var text = Text(value, key);
        return text == "True" || text == "true";
    }

    private static List<JsonElement> Items(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static string FullName(JsonElement person)
    {
        var parts = new[]
        {
            Text(person, "lastName"),
            Text(person, "firstName"),
            Text(person, "middleName")
        };
        return string.Join(' ', parts.Where(part => part.Length > 0));
    }

    private static string DisplayDate(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, out var date))
        {
            return date.ToString("dd.MM.yyyy");
        }

        return value;
    }

    private static string Time(JsonElement value, string key)
    {
        var time = Text(value, key);
        return time.Length >= 5 ? time[..5] : time;
    }

    private static string Money(JsonElement value, string key)
    {
        if (!decimal.TryParse(Text(value, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
        {
            return "—";
        }

        var format = amount == decimal.Truncate(amount) ? "N0" : "N2";
        return amount.ToString(format, CultureInfo.GetCultureInfo("ru-RU")) + " ₽";
    }

    private static string Snake(string key)
    {
        var result = "";
        for (var index = 0; index < key.Length; index++)
        {
            var character = key[index];
            if (char.IsUpper(character) && index > 0)
            {
                result += "_";
            }

            result += char.ToLowerInvariant(character);
        }

        return result;
    }

    private static Record Row(JsonElement json, string cell1, string cell2, string cell3, string cell4,
        string status, string details = "", Dictionary<string, string>? extra = null)
    {
        var values = new Dictionary<string, string>();
        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in json.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object)
                {
                    continue;
                }

                var value = property.Value.ValueKind == JsonValueKind.Null ? "" : property.Value.ToString();
                values.Add(Snake(property.Name), value);
            }
        }

        foreach (var key in values.Keys.ToArray())
        {
            if (values[key] is "True" or "False")
            {
                values[key] = values[key].ToLowerInvariant();
            }
        }

        if (extra is not null)
        {
            foreach (var (key, value) in extra)
            {
                values[key] = value;
            }
        }

        var id = Integer(json, "id");
        if (values.TryGetValue("id", out var recordId) && int.TryParse(recordId, out var number))
        {
            id = number;
        }

        return new Record
        {
            Id = id,
            Cell1 = cell1,
            Cell2 = cell2,
            Cell3 = cell3,
            Cell4 = cell4,
            Status = status,
            Details = details,
            Values = values
        };
    }

    private void Check(ClinicSession session)
    {
        if (_session is null || session.AccountId != _session.AccountId ||
            session.Role != _session.Role || session.PatientId != _session.PatientId ||
            session.EmployeeId != _session.EmployeeId)
        {
            throw new InvalidOperationException("Сессия завершена. Войдите снова.");
        }
    }

    private async Task<JsonElement> Request(string method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body is not null)
        {
            request.Content = body is HttpContent content ? content : JsonContent.Create(body);
        }
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
                    {
                        message = title;
                    }

                    var captcha = Property(error, "captcha");
                    if (path == "auth/login" && captcha.ValueKind == JsonValueKind.Object)
                    {
                        throw new CaptchaRequiredException(message, ParseChallenge(captcha));
                    }
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
            {
                return default;
            }

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
            var separator = path.Contains('?') ? "&" : "?";
            var pagePath = path + separator + "page=" + page + "&pageSize=200";
            var json = await Request("GET", pagePath);
            if (json.ValueKind == JsonValueKind.Array)
            {
                return Items(json);
            }

            var items = Items(Property(json, "items"));
            rows.AddRange(items);
            if (items.Count == 0 || rows.Count >= Integer(json, "total"))
            {
                return rows;
            }

            page++;
            if (page > 1000)
            {
                throw new InvalidOperationException("Слишком большой список. Уточните фильтр.");
            }
        }
    }

    public async Task<(ClinicSession Session, LiveDashboard Dashboard)?> SignInAsync(string login, string password)
    {
        _session = null;
        _http.DefaultRequestHeaders.Authorization = null;
        var result = await Request("POST", "auth/login", new
        {
            login,
            password,
            challengeId = _captchaId,
            captchaAnswer = _captchaAnswer
        });
        var account = Property(result, "userAccount");
        var role = Text(account, "roleCode") switch
        {
            "patient" => "Пациент",
            "registrar" => "Регистратор",
            "doctor" => "Врач",
            "admin" or "administrator" => "Администратор",
            _ => Text(account, "roleName")
        };
        if (role is not ("Пациент" or "Регистратор" or "Врач" or "Администратор"))
        {
            throw new InvalidOperationException("Эта роль не входит в приложение.");
        }

        _login = login;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Text(result, "token"));
        Permissions = Items(Property(result, "permissions")).Select(x => x.ToString()).ToArray();
        var employeeId = Integer(account, "employeeId");
        var patientId = Integer(account, "patientId");
        _session = new ClinicSession(
            Integer(account, "id"),
            employeeId == 0 ? null : employeeId,
            patientId == 0 ? null : patientId,
            Text(account, "login"),
            role);
        if (role != "Пациент" || Permissions.Contains("own.view"))
        {
            JsonElement person;
            if (role == "Пациент")
            {
                var profile = await Request("GET", "api/my/profile");
                person = Property(profile, "patient");
            }
            else
            {
                person = await Request("GET", "api/employees/" + _session.EmployeeId);
            }

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
        {
            throw new InvalidOperationException("Нет активной сессии.");
        }

        var response = await Request("GET", "api/me");
        var account = Property(response, "account");
        return Text(account, "userTheme") == "dark";
    }

    public async Task SaveUserThemeAsync(int accountId, bool dark)
    {
        if (_session?.AccountId != accountId)
        {
            throw new InvalidOperationException("Нет активной сессии.");
        }

        await Request("PUT", "api/me/theme", new { theme = dark ? "dark" : "light" });
    }

    public async Task ChangePasswordAsync(string oldPassword, string newPassword)
    {
        var login = _login ?? throw new InvalidOperationException("Войдите снова.");
        await Request("PUT", "api/me/password", new { oldPassword, newPassword });
        await SignInAsync(login, newPassword);
    }
}
