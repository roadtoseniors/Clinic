using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Clinic.Data;
using Clinic.Export;
using Clinic.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Npgsql;

namespace Clinic.ViewModels;

public partial class WorkspaceViewModel
{
    public ExportFile CreateExport(string action)
    {
        var all = action.StartsWith("export_all", StringComparison.Ordinal);
        var format = action.EndsWith("_excel", StringComparison.Ordinal) || action == "export_all"
            ? ExportFormat.Excel : action.EndsWith("_html", StringComparison.Ordinal) ? ExportFormat.Html : ExportFormat.Word;
        var table = ExportData(all);
        var extension = format switch { ExportFormat.Excel => "xlsx", ExportFormat.Word => "docx", _ => "html" };
        var bytes = format switch
        {
            ExportFormat.Excel => ExcelExporter.Create(table),
            ExportFormat.Word => WordExporter.Create(table),
            _ => System.Text.Encoding.UTF8.GetBytes(ExportHtml(all))
        };
        var mime = format switch
        {
            ExportFormat.Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ExportFormat.Word => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "text/html"
        };
        return new ExportFile($"{ExportValues.SafeName(Title)}-{table.CreatedAt:yyyyMMdd-HHmmss}.{extension}", mime, bytes);
    }

    public ExportTable ExportData(bool all)
    {
        if (!IsTable) throw new InvalidOperationException("Откройте раздел со списком записей.");
        var records = all ? Rows.ToArray() : [SelectedRow ?? throw new InvalidOperationException("Выберите запись для выгрузки.")];
        var headers = new[] { Header1, Header2, Header3, Header4, "Статус" };
        var indices = Enumerable.Range(0, headers.Length).Where(i => headers[i].Length > 0).ToArray();
        var columns = indices.Select(i => new ExportColumn(headers[i], ColumnKind(i))).ToArray();
        var rows = records.Select(row =>
        {
            var cells = new[] { row.Cell1, row.Cell2, row.Cell3, row.Cell4, row.Status };
            return indices.Select(i => cells[i]).ToArray();
        }).ToArray();
        var conditions = new List<string> { all ? $"Записей: {rows.Length}" : "Выбранная запись" };
        if (Search.Trim().Length > 0) conditions.Add("Поиск: " + Search.Trim());
        if (StatusFilter != "Все статусы") conditions.Add("Статус: " + StatusFilter);
        if (FilterFrom is { } from) conditions.Add("С " + from.ToString("dd.MM.yyyy"));
        if (FilterTo is { } to) conditions.Add("По " + to.ToString("dd.MM.yyyy"));
        if (SortOrder != "По умолчанию") conditions.Add(SortOrder);
        return new ExportTable(Title, string.Join(" · ", conditions), Session.Name, DateTime.Now,
            columns, rows, all ? "" : records[0].Details);
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
        string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        var title = all ? Title : SelectedRow?.Cell1 ?? Title;
        var body = all ? "<table><tr>" + string.Join("", new[] { Header1, Header2, Header3, Header4, "Статус" }.Select(h => "<th>" + E(h) + "</th>")) + "</tr>" + string.Join("", Rows.Select(r => "<tr>" + string.Join("", new[] { r.Cell1, r.Cell2, r.Cell3, r.Cell4, r.Status }.Select(x => "<td>" + E(x) + "</td>")) + "</tr>")) + "</table>" : "<p>" + E(SelectedRow?.Cell2 ?? "") + " · " + E(SelectedRow?.Cell3 ?? "") + " · " + E(SelectedRow?.Cell4 ?? "") + "</p><p>Статус: " + E(SelectedRow?.Status ?? "") + "</p><pre>" + E(SelectedRow?.Details ?? "") + "</pre>";
        return "<!doctype html><html lang='ru'><meta charset='utf-8'><title>" + E(title) + "</title><style>body{font:16px system-ui;max-width:960px;margin:48px auto;color:#172c43}h1{font-size:26px}pre{white-space:pre-wrap;font:inherit;line-height:1.6}table{width:100%;border-collapse:collapse}td,th{text-align:left;padding:12px;border:1px solid #dfe9f1}button{padding:10px 18px}@media print{button{display:none}body{margin:20px}}</style><h1>Медицинская информационная система</h1><h2>" + E(title) + "</h2>" + body + "<p>Сформировано: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm") + "</p><button onclick='window.print()'>Печать / сохранить PDF</button></html>";
    }
}
