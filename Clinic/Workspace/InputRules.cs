using System;
using System.Linq;

namespace Clinic.Workspace;
public static class InputRules
{
    public static string? Error(string key, string? raw)
    {
        var value = raw?.Trim() ?? "";
        if (value.Length == 0)
            return null;
        var limit = key switch
        {
            "last_name" or "first_name" or "middle_name" or "login" => 50,
            "phone" => 20,
            "snils" => 14,
            "address" => 200,
            "oms_policy" or "dms_policy" => 20,
            "password" or "new_password" => 100,
            _ => 0
        };
        if (limit > 0 && (key is "password" or "new_password" ? raw!.Length : value.Length) > limit)
            return $"Максимальная длина поля — {limit} символов.";
        if (key == "phone")
        {
            var digits = new string (value.Where(char.IsAsciiDigit).ToArray());
            if (digits.Length is < 10 or > 15 || value.Any(c => !char.IsAsciiDigit(c) && !"+()- ".Contains(c)) || value.Count(c => c == '+') > 1 || (value.Contains('+') && !value.StartsWith('+')))
                return "Телефон должен содержать от 10 до 15 цифр, например +7 (999) 123-45-67.";
        }

        if (key == "snils")
        {
            var digits = new string (value.Where(char.IsAsciiDigit).ToArray());
            if (digits.Length != 11 || value.Any(c => !char.IsAsciiDigit(c) && c is not ('-' or ' ')))
                return "СНИЛС должен содержать 11 цифр.";
        }

        if (key == "oms_policy" && (value.Length != 16 || !value.All(char.IsAsciiDigit)))
            return "Полис ОМС должен содержать 16 цифр.";
        if (key is "password" or "new_password" && System.Text.Encoding.UTF8.GetByteCount(raw!) > 72)
            return "Пароль слишком длинный: не более 72 байт в UTF-8.";
        if (key is "password" or "new_password" && (raw!.Length < 8 || !raw.Any(char.IsAsciiLetter) || !raw.Any(char.IsAsciiDigit)))
            return "Пароль должен содержать не менее 8 символов, латинскую букву и цифру.";
        return null;
    }
}
