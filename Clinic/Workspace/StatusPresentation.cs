namespace Clinic.Workspace;
public static class StatusPresentation
{
    public static string Tone(string status) => status.Trim().ToLowerInvariant() switch
    {
        "активен" or "активна" or "оплачен" or "закрыт" or "завершена" or "выполнено" or "готово" or "успешно" or "состоялся" or "оформлен" => "success",
        "отменена" or "отменено" or "неявка" or "заблокирован" => "error",
        "не оплачен" or "без счёта" or "ожидает" or "новое" or "выдано" => "warning",
        "архив" or "прочитано" or "аудит" or "справочник" => "neutral",
        _ => "info"
    };
}
