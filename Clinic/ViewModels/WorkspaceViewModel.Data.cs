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
    [RelayCommand]
    private async Task RefreshAsync() => await LoadPageAsync();
    private async Task LoadPageAsync()
    {
        var version = ++_loadVersion;
        var section = SelectedSection;
        if (section is null)
            return;
        IsBusy = true;
        Message = "";
        _loadFailed = false;
        try
        {
            if ((FromDate.Length > 0 && ParseDate(FromDate)is null) || (ToDate.Length > 0 && ParseDate(ToDate)is null))
                throw new InvalidOperationException("Введите даты фильтра в формате ГГГГ-ММ-ДД.");
            if (ParseDate(FromDate) > ParseDate(ToDate))
                throw new InvalidOperationException("Начало периода не может быть позже окончания.");
            if (section.Key == "home")
            {
                var dashboard = await Gateway.DashboardAsync(Session);
                if (version != _loadVersion)
                    return;
                Replace(Metrics, dashboard.Metrics);
                Replace(Events, dashboard.Items);
                EventTitle = dashboard.SectionTitle;
                Note = Catalog.WorkspaceNote(Session.Role);
                OnPropertyChanged(nameof(HasEvents));
            }
            else if (section.Key != "backup")
            {
                var records = await Gateway.ListAsync(Session, section.Key, ParseDate(FromDate), ParseDate(ToDate));
                if (version != _loadVersion)
                    return;
                var selectedId = SelectedRow?.Id;
                var selectedTable = SelectedRow?.Values.GetValueOrDefault("table");
                _allRows = records;
                var currentStatus = StatusFilter;
                Replace(Statuses, new[] { "Все статусы" }.Concat(records.Select(r => r.Status).Distinct()));
                StatusFilter = Statuses.Contains(currentStatus) ? currentStatus : "Все статусы";
                Filter();
                SelectedRow = Rows.FirstOrDefault(r => r.Id == selectedId && r.Values.GetValueOrDefault("table") == selectedTable);
            }
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                _loadFailed = true;
                ShowError(Friendly(ex));
            }
        }
        finally
        {
            if (version == _loadVersion)
                IsBusy = false;
        }
    }

    private void Filter()
    {
        var search = Search.Trim();
        var from = ParseDate(FromDate);
        var to = ParseDate(ToDate);
        var rows = _allRows.Where(r => (search.Length == 0 || string.Join(' ', r.Cell1, r.Cell2, r.Cell3, r.Cell4, r.Details, r.Values.GetValueOrDefault("oms_policy"), r.Values.GetValueOrDefault("card_number")).Contains(search, StringComparison.OrdinalIgnoreCase)) && (StatusFilter == "Все статусы" || r.Status == StatusFilter) && (PatientFilter is null || (r.Values.GetValueOrDefault("patient_id") == PatientFilter.ToString())) && (SelectedSection?.Key == "reports" || (from is null && to is null) || MatchesDate(r, from, to)));
        rows = SortOrder switch
        {
            "По названию: А–Я" => rows.OrderBy(r => r.Cell1, StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), true)),
            "По названию: Я–А" => rows.OrderByDescending(r => r.Cell1, StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), true)),
            "Сначала новые" => rows.OrderByDescending(RecordDate).ThenByDescending(r => r.Id),
            "Сначала старые" => rows.OrderBy(RecordDate).ThenBy(r => r.Id),
            _ => rows
        };
        NotifyFilters();
        Replace(Rows, rows);
        if (IsReports)
            Replace(ReportCharts, ReportChart.Build(Rows));
        else
            ReportCharts.Clear();
        if (SelectedRow is not null && !Rows.Contains(SelectedRow))
            SelectedRow = null;
        foreach (var p in new[]
        {
            nameof(HasRows),
            nameof(IsEmpty),
            nameof(RowCount),
            nameof(EmptyTitle),
            nameof(EmptyDescription)
        }

        )
            OnPropertyChanged(p);
    }

    private static DateTime? ParseDate(string text) => DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    private static DateTime? RecordDate(Record row)
    {
        var value = new[]
        {
            "work_date",
            "visited_at",
            "issued_at",
            "created_at",
            "rendered_at",
            "done_at",
            "acted_at",
            "birth_date",
            "last_login"
        }.Select(k => row.Values.GetValueOrDefault(k, "")).FirstOrDefault(v => v.Length > 0);
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, out var date))
            return null;
        if (row.Values.ContainsKey("work_date") && TimeOnly.TryParse(row.Values.GetValueOrDefault("start_time", ""), out var time))
            return date.Date + time.ToTimeSpan();
        return date;
    }

    private static bool MatchesDate(Record row, DateTime? from, DateTime? to) => RecordDate(row)is { } date && (from is null || date.Date >= from.Value.Date) && (to is null || date.Date <= to.Value.Date);
    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        await RefreshAsync();
        AreFiltersOpen = false;
    }

    [RelayCommand]
    private void ResetFilters()
    {
        Search = "";
        StatusFilter = "Все статусы";
        FromDate = "";
        ToDate = "";
        PatientFilter = null;
        Filter();
        if (SelectedSection?.Key == "reports")
            _ = RefreshAsync();
    }
}
