using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Clinic.Export;
using Clinic.Workspace;

namespace Clinic.ViewModels;

public partial class WorkspaceViewModel
{
    public ExportFile CreateExport(string action)
    {
        var all = action.StartsWith("export_all", StringComparison.Ordinal);
        var table = ExportData(all);
        string extension;
        string contentType;
        byte[] content;

        if (action.EndsWith("_excel", StringComparison.Ordinal) || action == "export_all")
        {
            extension = "xlsx";
            contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            content = ExcelExporter.Create(table);
        }
        else if (action.EndsWith("_html", StringComparison.Ordinal))
        {
            extension = "html";
            contentType = "text/html";
            content = Encoding.UTF8.GetBytes(ExportHtml(all));
        }
        else
        {
            extension = "docx";
            contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            content = WordExporter.Create(table);
        }

        var name = $"{ExportValues.SafeName(Title)}-{table.CreatedAt:yyyyMMdd-HHmmss}.{extension}";
        return new ExportFile(name, contentType, content);
    }

    public ExportTable ExportData(bool all)
    {
        if (!IsTable)
        {
            throw new InvalidOperationException("Откройте раздел со списком записей.");
        }

        var records = new List<Record>();
        if (all)
        {
            // В Rows уже применены поиск, фильтры и сортировка.
            records.AddRange(Rows);
        }
        else
        {
            if (SelectedRow is null)
            {
                throw new InvalidOperationException("Выберите запись для выгрузки.");
            }
            records.Add(SelectedRow);
        }

        var headers = new[] { Header1, Header2, Header3, Header4, "Статус" };
        var columns = new List<ExportColumn>();
        var columnNumbers = new List<int>();
        for (var column = 0; column < headers.Length; column++)
        {
            if (headers[column].Length == 0)
            {
                continue;
            }
            columns.Add(new ExportColumn(headers[column], ColumnKind(column)));
            columnNumbers.Add(column);
        }

        var rows = new List<string[]>();
        foreach (var record in records)
        {
            var cells = new[] { record.Cell1, record.Cell2, record.Cell3, record.Cell4, record.Status };
            rows.Add(columnNumbers.Select(column => cells[column]).ToArray());
        }

        var conditions = new List<string>();
        conditions.Add(all ? $"Записей: {rows.Count}" : "Выбранная запись");
        if (Search.Trim().Length > 0)
        {
            conditions.Add("Поиск: " + Search.Trim());
        }
        if (StatusFilter != "Все статусы")
        {
            conditions.Add("Статус: " + StatusFilter);
        }
        if (FilterFrom is DateTime from)
        {
            conditions.Add("С " + from.ToString("dd.MM.yyyy"));
        }
        if (FilterTo is DateTime to)
        {
            conditions.Add("По " + to.ToString("dd.MM.yyyy"));
        }
        if (SortOrder != "По умолчанию")
        {
            conditions.Add(SortOrder);
        }

        var details = all ? "" : records[0].Details;
        return new ExportTable(Title, string.Join(" · ", conditions), Session.Name,
            DateTime.Now, columns, rows, details);
    }

    private ExportCellKind ColumnKind(int column) => (SelectedSection?.Key, column) switch
    {
        ("patients", 1) or ("schedule", 0) or ("visits", 0) or ("referrals", 2)
            or ("rendered", 2) or ("documents", 2) or ("notifications", 1) or ("accounts", 3) => ExportCellKind.Date,
        ("appointments", 0) or ("slots", 0) or ("audit", 0) => ExportCellKind.DateTime,
        ("rendered", 3) or ("invoices", 2) or ("invoices", 3) or ("services", 2) => ExportCellKind.Money,
        ("reports", 1) => ExportCellKind.Metric,
        _ => ExportCellKind.Text
    };

    public string ExportHtml(bool all = false)
    {
        var data = ExportData(all);
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html lang='ru'><head><meta charset='utf-8'>");
        html.Append("<title>").Append(WebUtility.HtmlEncode(data.Title)).AppendLine("</title>");
        html.AppendLine("""
            <style>
              body { font: 16px system-ui; max-width: 960px; margin: 48px auto; color: #172c43; }
              h1 { font-size: 26px; }
              pre { white-space: pre-wrap; font: inherit; line-height: 1.6; }
              table { width: 100%; border-collapse: collapse; }
              td, th { text-align: left; padding: 12px; border: 1px solid #dfe9f1; }
              button { padding: 10px 18px; }
              @media print { button { display: none; } body { margin: 20px; } }
            </style></head><body>
            <h1>Медицинская информационная система</h1>
            """);
        html.Append("<h2>").Append(WebUtility.HtmlEncode(data.Title)).AppendLine("</h2>");
        html.Append("<p>").Append(WebUtility.HtmlEncode(data.Context)).AppendLine("</p>");
        html.AppendLine("<table><tr>");
        foreach (var column in data.Columns)
        {
            html.Append("<th>").Append(WebUtility.HtmlEncode(column.Title)).AppendLine("</th>");
        }
        html.AppendLine("</tr>");
        foreach (var row in data.Rows)
        {
            html.AppendLine("<tr>");
            foreach (var cell in row)
            {
                html.Append("<td>").Append(WebUtility.HtmlEncode(cell)).AppendLine("</td>");
            }
            html.AppendLine("</tr>");
        }
        html.AppendLine("</table>");
        if (data.Rows.Count == 0)
        {
            html.AppendLine("<p>Нет записей по выбранным условиям</p>");
        }
        if (data.Details.Length > 0)
        {
            html.Append("<pre>").Append(WebUtility.HtmlEncode(data.Details)).AppendLine("</pre>");
        }
        html.AppendLine($"<p>Сформировано: {data.CreatedAt:dd.MM.yyyy HH:mm}</p>");
        html.AppendLine("<button onclick='window.print()'>Печать / сохранить PDF</button>");
        html.AppendLine("</body></html>");
        return html.ToString();
    }
}
