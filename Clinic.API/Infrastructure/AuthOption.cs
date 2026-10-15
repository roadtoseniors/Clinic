using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Clinic.API;

public class AuthOption
{
    public const string ISSUER = "Masha";
    public const string AUDIENCE = "Vadim";
    public static string SigningKey { get; set; } = "";

    // Сколько живёт токен. Права из role_permission попадают в токен при входе,
    // поэтому изменения прав вступают в силу после повторного входа.
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(12);

    public static SymmetricSecurityKey GetSymmetricSecurityKey() =>
        SigningKey.Length >= 32 ? new(Encoding.UTF8.GetBytes(SigningKey))
            : throw new InvalidOperationException("Auth:SigningKey / CLINIC_JWT_KEY: минимум 32 символа.");
}

public record AuthData(string Login, string Password);
