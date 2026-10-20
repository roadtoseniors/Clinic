using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace Clinic.Export;

public static class ExcelExporter
{
    public static byte[] Create(ExportTable table)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Author = table.Author;
        workbook.Properties.Title = table.Title;
        workbook.Properties.Created = table.CreatedAt;
        var sheetName = new string(table.Title.Where(c => !"[]:*?/\\".Contains(c)).ToArray()).Trim('\'');
        var sheet = workbook.AddWorksheet(string.IsNullOrWhiteSpace(sheetName) ? "Данные" : sheetName[..Math.Min(31, sheetName.Length)]);
        sheet.Style.Font.FontName = "Calibri";
        sheet.Style.Font.FontSize = 11;
        sheet.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        sheet.Style.Alignment.WrapText = true;
        var columns = table.Columns.Count;

        sheet.Range(1, 1, 1, columns).Merge().FirstCell().SetValue(table.Title);
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(18);
        sheet.Row(1).Height = 30;
        sheet.Range(2, 1, 2, columns).Merge().FirstCell().SetValue(table.Context);
        sheet.Row(2).Height = 32;
        sheet.Range(3, 1, 3, columns).Merge().FirstCell()
            .SetValue($"Сформировано {table.CreatedAt:dd.MM.yyyy HH:mm} · {table.Author}");
        sheet.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#617583");
        sheet.Row(3).Height = 22;

        const int headerRow = 5;
        for (var column = 0; column < columns; column++)
        {
            sheet.Cell(headerRow, column + 1).SetValue(table.Columns[column].Title);
            // Fixed readable widths also work in the browser without installed desktop fonts.
            sheet.Column(column + 1).Width = ExportValues.ColumnWidth(table, column);
        }
        var header = sheet.Range(headerRow, 1, headerRow, columns);
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#11546A");
        header.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
        sheet.Row(headerRow).Height = 32;

        for (var row = 0; row < table.Rows.Count; row++)
        {
            var rowNumber = headerRow + row + 1;
            var height = 28d;
            for (var column = 0; column < columns; column++)
            {
                var text = table.Rows[row][column];
                WriteCell(sheet.Cell(rowNumber, column + 1), text, table.Columns[column].Kind);
                height = Math.Max(height, 16 * Math.Ceiling(text.Length / Math.Max(8, sheet.Column(column + 1).Width - 3)) + 10);
            }
            sheet.Row(rowNumber).Height = Math.Min(409, height);
            if (row % 2 == 1) sheet.Range(rowNumber, 1, rowNumber, columns).Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F6F8");
        }

        var lastRow = headerRow + table.Rows.Count;
        var grid = sheet.Range(headerRow, 1, Math.Max(headerRow, lastRow), columns);
        grid.Style.Alignment.Indent = 1;
        grid.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        grid.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        grid.Style.Border.InsideBorderColor = XLColor.FromHtml("#D7E1E7");
        grid.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        grid.Style.Border.OutsideBorderColor = XLColor.FromHtml("#D7E1E7");
        if (table.Rows.Count > 0) sheet.Range(headerRow, 1, lastRow, columns).SetAutoFilter();
        else
        {
            lastRow++;
            sheet.Range(lastRow, 1, lastRow, columns).Merge().FirstCell().SetValue("Нет записей по выбранным условиям");
            sheet.Row(lastRow).Height = 28;
        }
        sheet.SheetView.FreezeRows(headerRow);
        sheet.ShowGridLines = false;

        if (table.Details.Length > 0)
        {
            var row = lastRow + 2;
            sheet.Range(row, 1, row, columns).Merge().FirstCell().SetValue("Подробности").Style.Font.SetBold();
            var capacity = Math.Max(40, (int)table.Columns.Select((_, i) => sheet.Column(i + 1).Width).Sum() - 5);
            foreach (var line in table.Details.Replace("\r", "").Split('\n'))
            {
                // Split long notes into visible rows instead of clipping a tall merged cell.
                for (var offset = 0; offset < Math.Max(1, line.Length); offset += capacity)
                {
                    row++;
                    var text = line.Substring(Math.Min(offset, line.Length), Math.Min(capacity, line.Length - offset));
                    sheet.Range(row, 1, row, columns).Merge().FirstCell().SetValue(text);
                    sheet.Row(row).Height = 28;
                }
            }
            lastRow = row;
        }

        sheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
        sheet.PageSetup.FitToPages(1, 0);
        sheet.PageSetup.SetRowsToRepeatAtTop(1, headerRow);
        sheet.PageSetup.PrintAreas.Add(1, 1, lastRow, columns);
        sheet.PageSetup.Footer.Center.AddText(XLHFPredefinedText.PageNumber);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteCell(IXLCell cell, string text, ExportCellKind kind)
    {
        if (text.Length > 32767) throw new InvalidOperationException("Текст слишком длинный для ячейки Excel. Выгрузите эту запись в Word.");
        if (kind is ExportCellKind.Date or ExportCellKind.DateTime && ExportValues.TryDate(text, out var date))
        {
            cell.Value = date;
            cell.Style.DateFormat.Format = kind == ExportCellKind.DateTime ? "dd.mm.yyyy hh:mm" : "dd.mm.yyyy";
        }
        else if (kind is ExportCellKind.Money or ExportCellKind.Metric && ExportValues.TryNumber(text, out var number))
        {
            cell.Value = (double)(text.Contains('%') ? number / 100 : number);
            cell.Style.NumberFormat.Format = text.Contains('%') ? "0.0%" : kind == ExportCellKind.Money || text.Contains('₽') ? "#,##0.00 \"₽\"" : "0.##";
        }
        else
        {
            // Policy numbers, phone numbers and strings starting with '=' stay literal text.
            cell.SetValue(text);
        }
    }
}
