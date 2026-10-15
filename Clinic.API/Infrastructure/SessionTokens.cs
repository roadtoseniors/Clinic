using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Clinic.API;

public sealed class SessionTokens
{
    public static async Task RevokeAsync(ClaimsPrincipal user, MyDbContext db, CancellationToken ct)
    {
        var id = user.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (id is null) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO revoked_session(id,expires_at) VALUES({id},{DateTimeOffset.UtcNow.Add(AuthOption.TokenLifetime)}) ON CONFLICT(id) DO NOTHING", ct);
        await db.RevokedSessions.Where(s => s.ExpiresAt < DateTimeOffset.UtcNow).ExecuteDeleteAsync(ct);
    }
    public static string Stamp(string passwordHash, int roleId, IEnumerable<string> permissions) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash + "|" + roleId + "|" + string.Join('|', permissions.Order()))));
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var user = context.Principal!;
        var tokenId = user.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        if (string.IsNullOrEmpty(tokenId)) { context.Fail("Сессия завершена. Войдите снова."); return; }
        var db = context.HttpContext.RequestServices.GetRequiredService<MyDbContext>();
        var account = await db.Accounts.AsNoTracking().Where(a => a.Id == user.GetAccountId())
            .Select(a => new { a.IsActive, a.PasswordHash, a.RoleId,
                OwnerActive = a.Patient != null ? a.Patient.IsActive : a.Employee != null && a.Employee.IsActive,
                Permissions = a.Role.PermissionCodes.Select(p => p.Code).ToList() }).FirstOrDefaultAsync(context.HttpContext.RequestAborted);
        if (user.FindFirst("serverEpoch")?.Value != await RuntimeSchema.EpochAsync(db,context.HttpContext.RequestAborted) || account is null || !account.IsActive || !account.OwnerActive ||
            await db.RevokedSessions.AnyAsync(s => s.Id == tokenId, context.HttpContext.RequestAborted) ||
            user.FindFirst("sessionStamp")?.Value != Stamp(account.PasswordHash, account.RoleId, account.Permissions))
            context.Fail("Сессия завершена. Войдите снова.");
    }
}
