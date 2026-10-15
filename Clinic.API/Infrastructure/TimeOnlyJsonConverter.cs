using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clinic.API;

// Стандартный JSON в .NET понимает время только как "10:30:00".
// Этот конвертер принимает и "10:30", и "10:30:00", а отдаёт всегда "10:30".
public sealed class TimeOnlyJsonConverter : JsonConverter<TimeOnly>
{
    private static readonly string[] Formats = { "HH:mm", "HH:mm:ss", "H:mm", "HH:mm:ss.FFFFFFF" };

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (TimeOnly.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time;
        throw new JsonException($"Неверное время: {text}. Ожидается формат ЧЧ:мм");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("HH:mm", CultureInfo.InvariantCulture));
}
