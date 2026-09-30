using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Clinic.API;

public class AuthOption
{
    public const string ISSUER = "Masha";
    public const string AUDIENCE = "Vadim";
    private const string KEY = "Pevt_KiloPevt_MegaPevt_GigoPevt_TeraPevt_PetaPevt_1!_4!_8!_8!";
    
    public static SymmetricSecurityKey GetSymmetricSecurityKey() =>
        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(KEY));
}

public record AuthData(string Login, string Password);