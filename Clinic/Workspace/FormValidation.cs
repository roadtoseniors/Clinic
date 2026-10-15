using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Clinic.ViewModels;

namespace Clinic.Workspace;
public static class FormValidation
{
    public static void Validate(IEnumerable<EditorField> source)
    {
        var fields = source.ToArray();
        string Value(string key) => fields.FirstOrDefault(f => f.Spec.Key == key)?.Value ?? "";
        foreach (var field in fields)
        {
            var value = field.Value;
            if (field.Spec.Required && string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Заполните поле «{field.Spec.Label}».");
            if (value.Length == 0)
                continue;
            if (InputRules.Error(field.Spec.Key, value)is string error)
                throw new InvalidOperationException($"{field.Spec.Label}: {error}");
            if (field.IsDate && !DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new InvalidOperationException($"Выберите корректную дату в поле «{field.Spec.Label}».");
            if (field.Spec.Key == "birth_date" && DateTime.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture) > DateTime.Today)
                throw new InvalidOperationException("Дата рождения не может быть в будущем.");
            if (field.Spec.Key is "password" or "new_password" && value.Length < 8)
                throw new InvalidOperationException("Пароль должен содержать не менее 8 символов.");
            if (field.Spec.Key == "oms_policy" && (value.Length != 16 || !value.All(char.IsAsciiDigit)))
                throw new InvalidOperationException("Полис ОМС должен содержать 16 цифр.");
            if (field.Spec.Key is "price" or "amount" && (!decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) || number < 0 || (field.Spec.Key == "amount" && number == 0)))
                throw new InvalidOperationException($"Укажите корректную сумму в поле «{field.Spec.Label}».");
            if (field.Spec.Key is "quantity" or "slot_minutes" && (!int.TryParse(value, out var count) || count <= 0))
                throw new InvalidOperationException($"В поле «{field.Spec.Label}» нужно целое число больше нуля.");
            if (field.Spec.Key is "start_time" or "end_time" && !TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new InvalidOperationException($"Укажите время в формате ЧЧ:ММ в поле «{field.Spec.Label}».");
        }

        foreach (var pair in new[]
        {
            ("password", "password_confirmation"),
            ("new_password", "confirmation")
        }

        )
            if (fields.Any(f => f.Spec.Key == pair.Item2) && Value(pair.Item1) != Value(pair.Item2))
                throw new InvalidOperationException("Пароли не совпадают.");
        if (Value("start_time").Length > 0 && Value("end_time").Length > 0 && TimeOnly.Parse(Value("start_time")) >= TimeOnly.Parse(Value("end_time")))
            throw new InvalidOperationException("Окончание работы должно быть позже начала.");
        if (Value("dms_policy").Length > 0 && fields.Any(f => f.Spec.Key == "dms_company_id") && Value("dms_company_id").Length == 0)
            throw new InvalidOperationException("Для полиса ДМС выберите страховую компанию.");
        if (fields.Any(f => f.Spec.Key == "district_id") && ((Value("district_id").Length > 0) != (Value("primary_doctor_id").Length > 0)))
            throw new InvalidOperationException("Для прикрепления выберите участок и участкового врача.");
    }
}
