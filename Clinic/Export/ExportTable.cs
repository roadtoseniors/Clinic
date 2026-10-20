using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Clinic.Export;

public enum ExportCellKind { Text, Date, DateTime, Money, Metric }
public enum ExportFormat { Word, Excel, Html }
public sealed record ExportColumn(string Title, ExportCellKind Kind = ExportCellKind.Text);
public sealed record ExportTable(string Title, string Context, string Author, DateTime CreatedAt,
    IReadOnlyList<ExportColumn> Columns, IReadOnlyList<string[]> Rows, string Details = "");
public sealed record ExportFile(string Name, string ContentType, byte[] Content);

internal static class ExportValues
{
    public static bool TryNumber(string text, out decimal number)
    {
        var cleaned = text.Replace("₽", "").Replace("%", "").Replace(" ", "")
            .Replace("\u00A0", "").Replace("\u202F", "").Replace(',', '.').Trim();
        return decimal.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out number);
    }

    public static bool TryDate(string text, out DateTime date)
    {
        var parsed = DateTime.TryParseExact(text,
            ["dd.MM.yyyy", "dd.MM.yyyy HH:mm", "yyyy-MM-dd", "yyyy-MM-dd HH:mm"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        if (!parsed && text.Contains('T'))
            parsed = DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date);
        return parsed && date.Year >= 1900;
    }

    public static double ColumnWidth(ExportTable table, int column)
    {
        var length = table.Rows.Select(row => row[column].Length).Append(table.Columns[column].Title.Length).Max();
        return Math.Clamp(length + 3, 16, 38);
    }

    public static string SafeName(string title)
    {
        var name = new string(title.Select(c => "\\/:*?\"<>|".Contains(c) || char.IsControl(c) ? '-' : c).ToArray()).Trim(' ', '.');
        return name.Length == 0 ? "Поликлиника" : name[..Math.Min(name.Length, 70)];
    }
}
