using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Clinic.Context;
using Clinic.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MyDbContext>();
builder.Services.AddCors();

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

app.MapGet("/api/roles", async (MyDbContext cnt) =>
{
    try
    {
        var roles = await cnt.Roles.ToListAsync();
        return Results.Ok(roles);
    }
    catch (Exception ex)
    {
        Console.WriteLine(ex.Message);
        return Results.Problem("Ошибка при получении данных");
    }
});

app.MapGet("/api/users", (MyDbContext cnt) =>
{

});

public class AuthOption
{
    public const string ISSUER = "Masha";
    public const string AUDIENCE = "Vadim";
    private const string KEY = "Pevt_KiloPevt_MegaPevt_GigoPevt_TeraPevt_PetaPevt_1!_4!_8!_8!";
    
    public static SymmetricSecurityKey GetSymmetricSecurityKey() =>
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(KEY));
}

public record AuthData(string Login, string Password);