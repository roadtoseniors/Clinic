using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Clinic.Data;
using Clinic.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Npgsql;

namespace Clinic.ViewModels;

public partial class WorkspaceViewModel
{
    public string ExportHtml(bool all = false)
    {
        string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        var title = all ? Title : SelectedRow?.Cell1 ?? Title;
        var body = all ? "<table><tr>" + string.Join("", new[] { Header1, Header2, Header3, Header4, "Статус" }.Select(h => "<th>" + E(h) + "</th>")) + "</tr>" + string.Join("", Rows.Select(r => "<tr>" + string.Join("", new[] { r.Cell1, r.Cell2, r.Cell3, r.Cell4, r.Status }.Select(x => "<td>" + E(x) + "</td>")) + "</tr>")) + "</table>" : "<p>" + E(SelectedRow?.Cell2 ?? "") + " · " + E(SelectedRow?.Cell3 ?? "") + " · " + E(SelectedRow?.Cell4 ?? "") + "</p><p>Статус: " + E(SelectedRow?.Status ?? "") + "</p><pre>" + E(SelectedRow?.Details ?? "") + "</pre>";
        return "<!doctype html><html lang='ru'><meta charset='utf-8'><title>" + E(title) + "</title><style>body{font:16px system-ui;max-width:960px;margin:48px auto;color:#172c43}h1{font-size:26px}pre{white-space:pre-wrap;font:inherit;line-height:1.6}table{width:100%;border-collapse:collapse}td,th{text-align:left;padding:12px;border:1px solid #dfe9f1}button{padding:10px 18px}@media print{button{display:none}body{margin:20px}}</style><h1>Медицинская информационная система</h1><h2>" + E(title) + "</h2>" + body + "<p>Сформировано: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm") + "</p><button onclick='window.print()'>Печать / сохранить PDF</button></html>";
    }
}
