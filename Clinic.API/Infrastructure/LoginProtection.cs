using System.Security.Cryptography;
using System.Text;

namespace Clinic.API;

public sealed record CaptchaDto(string ChallengeId, string ImageBase64, DateTimeOffset ExpiresAt);
public sealed record CaptchaRequest(string Login);

public sealed class LoginProtection
{
    private sealed class Attempts
    {
        public int Failures;
        public DateTimeOffset LastSeen;
        public string Answer = "";
        public CaptchaDto? Captcha;
    }

    private readonly Dictionary<string, Attempts> _attempts = new();
    private readonly object _sync = new();

    public static string Key(string login, string? address)
    {
        var text = login.Trim().ToUpperInvariant() + "|" + address;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public bool Accepts(string key, string? id, string? answer)
    {
        // Несколько запросов могут прийти одновременно, поэтому защищаем словарь.
        lock (_sync)
        {
            var attempts = GetAttempts(key);
            if (attempts.Failures < 3)
            {
                return true;
            }

            var captcha = attempts.Captcha;
            var valid = captcha is not null
                && captcha.ChallengeId == id
                && captcha.ExpiresAt > DateTimeOffset.UtcNow
                && string.Equals(attempts.Answer, answer?.Trim(), StringComparison.OrdinalIgnoreCase);

            // Картинку можно использовать только один раз.
            attempts.Captcha = null;
            attempts.Answer = "";
            return valid;
        }
    }

    public CaptchaDto? Failed(string key)
    {
        lock (_sync)
        {
            var attempts = GetAttempts(key);
            attempts.Failures++;
            if (attempts.Failures < 3)
            {
                return null;
            }
            return CreateCaptcha(attempts);
        }
    }

    public CaptchaDto? Refresh(string key)
    {
        lock (_sync)
        {
            var attempts = GetAttempts(key);
            if (attempts.Failures < 3)
            {
                return null;
            }
            return CreateCaptcha(attempts);
        }
    }

    public void Reset(string key)
    {
        lock (_sync)
        {
            _attempts.Remove(key);
        }
    }

    private Attempts GetAttempts(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredKeys = _attempts
            .Where(item => now - item.Value.LastSeen > TimeSpan.FromMinutes(15))
            .Select(item => item.Key)
            .ToArray();
        foreach (var expiredKey in expiredKeys)
        {
            _attempts.Remove(expiredKey);
        }

        if (!_attempts.TryGetValue(key, out var attempts))
        {
            attempts = new Attempts();
            _attempts.Add(key, attempts);
        }
        attempts.LastSeen = now;
        return attempts;
    }

    private static CaptchaDto CreateCaptcha(Attempts attempts)
    {
        const string alphabet = "23456789ACDEFHJKLMNPRTXY";
        var letters = new char[5];
        for (var i = 0; i < letters.Length; i++)
        {
            letters[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        var code = new string(letters);
        var image = Convert.ToBase64String(CaptchaImageGenerator.Create(code));
        var captcha = new CaptchaDto(Guid.NewGuid().ToString("N"), image, DateTimeOffset.UtcNow.AddMinutes(3));
        attempts.Answer = code;
        attempts.Captcha = captcha;
        return captcha;
    }
}
