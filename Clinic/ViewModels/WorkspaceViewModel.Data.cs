using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Clinic.Data;
using Clinic.Workspace;
using CommunityToolkit.Mvvm.Input;

namespace Clinic.ViewModels;

public partial class WorkspaceViewModel
{
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadPageAsync();
    }

    private async Task LoadPageAsync()
    {
        var loadVersion = ++_loadVersion;
        var section = SelectedSection;
        if (section is null)
        {
            return;
        }

        IsBusy = true;
        Message = "";
        _loadFailed = false;
        try
        {
            var fromDate = ParseDate(FromDate);
            var toDate = ParseDate(ToDate);
            if ((FromDate.Length > 0 && fromDate is null) || (ToDate.Length > 0 && toDate is null))
            {
                throw new InvalidOperationException("Введите даты фильтра в формате ГГГГ-ММ-ДД.");
            }

            if (fromDate > toDate)
            {
                throw new InvalidOperationException("Начало периода не может быть позже окончания.");
            }

            if (section.Key == "home")
            {
                var dashboard = await Gateway.DashboardAsync(Session);
                // За время запроса пользователь мог открыть другую страницу.
                if (loadVersion != _loadVersion)
                {
                    return;
                }

                Replace(Metrics, dashboard.Metrics);
                Replace(Events, dashboard.Items);
                EventTitle = dashboard.SectionTitle;
                Note = Catalog.WorkspaceNote(Session.Role);
                OnPropertyChanged(nameof(HasEvents));
            }
            else if (section.Key != "backup")
            {
                var records = await Gateway.ListAsync(Session, section.Key, fromDate, toDate);
                if (loadVersion != _loadVersion)
                {
                    return;
                }

                var selectedId = SelectedRow?.Id;
                var selectedTable = SelectedRow?.Values.GetValueOrDefault("table");
                _allRows = records;
                var currentStatus = StatusFilter;
                Replace(Statuses, new[] { "Все статусы" }.Concat(records.Select(row => row.Status).Distinct()));
                StatusFilter = Statuses.Contains(currentStatus) ? currentStatus : "Все статусы";
                Filter();
                SelectedRow = Rows.FirstOrDefault(row =>
                    row.Id == selectedId && row.Values.GetValueOrDefault("table") == selectedTable);
            }
        }
        catch (Exception ex)
        {
            if (loadVersion == _loadVersion)
            {
                _loadFailed = true;
                ShowError(Friendly(ex));
            }
        }
        finally
        {
            if (loadVersion == _loadVersion)
            {
                IsBusy = false;
            }
        }
    }

    private void Filter()
    {
        var search = Search.Trim();
        var fromDate = ParseDate(FromDate);
        var toDate = ParseDate(ToDate);
        IEnumerable<Record> rows = _allRows;

        if (search.Length > 0)
        {
            rows = rows.Where(row =>
            {
                var searchableText = string.Join(' ', row.Cell1, row.Cell2, row.Cell3, row.Cell4,
                    row.Details, row.Values.GetValueOrDefault("oms_policy"),
                    row.Values.GetValueOrDefault("card_number"));
                return searchableText.Contains(search, StringComparison.OrdinalIgnoreCase);
            });
        }

        if (StatusFilter != "Все статусы")
        {
            rows = rows.Where(row => row.Status == StatusFilter);
        }

        if (PatientFilter is not null)
        {
            var patientId = PatientFilter.ToString();
            rows = rows.Where(row => row.Values.GetValueOrDefault("patient_id") == patientId);
        }

        // Период отчёта уже учтён при запросе данных с сервера.
        if (SelectedSection?.Key != "reports" && (fromDate is not null || toDate is not null))
        {
            rows = rows.Where(row => MatchesDate(row, fromDate, toDate));
        }

        var russianComparer = StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), true);
        rows = SortOrder switch
        {
            "По названию: А–Я" => rows.OrderBy(row => row.Cell1, russianComparer),
            "По названию: Я–А" => rows.OrderByDescending(row => row.Cell1, russianComparer),
            "Сначала новые" => rows.OrderByDescending(RecordDate).ThenByDescending(row => row.Id),
            "Сначала старые" => rows.OrderBy(RecordDate).ThenBy(row => row.Id),
            _ => rows
        };
        NotifyFilters();
        Replace(Rows, rows);
        if (IsReports)
        {
            Replace(ReportCharts, ReportChart.Build(Rows));
        }
        else
        {
            ReportCharts.Clear();
        }

        if (SelectedRow is not null && !Rows.Contains(SelectedRow))
        {
            SelectedRow = null;
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(RowCount));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));
    }

    private static DateTime? ParseDate(string text)
    {
        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
        {
            return date;
        }

        return null;
    }

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
        }.Select(key => row.Values.GetValueOrDefault(key, "")).FirstOrDefault(text => text.Length > 0);
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, out var date))
        {
            return null;
        }

        if (row.Values.ContainsKey("work_date") && TimeOnly.TryParse(row.Values.GetValueOrDefault("start_time", ""), out var time))
        {
            return date.Date + time.ToTimeSpan();
        }

        return date;
    }

    private static bool MatchesDate(Record row, DateTime? from, DateTime? to)
    {
        var date = RecordDate(row);
        if (date is null)
        {
            return false;
        }

        var afterStart = from is null || date.Value.Date >= from.Value.Date;
        var beforeEnd = to is null || date.Value.Date <= to.Value.Date;
        return afterStart && beforeEnd;
    }

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
        {
            _ = RefreshAsync();
        }
    }
}
