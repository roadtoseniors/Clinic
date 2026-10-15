using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Clinic.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Clinic.ViewModels;

public partial class WorkspaceViewModel
{
    [RelayCommand] private void ToggleExtraNavigation() => IsExtraNavigationOpen = !IsExtraNavigationOpen;
    [RelayCommand] private void ToggleClinicalNavigation() => IsClinicalNavigationOpen = !IsClinicalNavigationOpen;
    public bool HasDayScope => SelectedSection?.Key is "appointments" or "schedule" or "slots";
    public string DateScopeLabel => FromDate == DateTime.Today.ToString("yyyy-MM-dd") && ToDate == FromDate ? "Сегодня" : FromDate.Length == 0 && ToDate.Length == 0 ? "Все даты" : "Выбранный период";

    [RelayCommand]
    private async Task SetDateRangeAsync(string range)
    {
        FromDate = range == "all" ? "" : DateTime.Today.ToString("yyyy-MM-dd");
        ToDate = range == "all" ? "" : DateTime.Today.AddDays(range == "week" ? 6 : 0).ToString("yyyy-MM-dd");
        await RefreshAsync();
    }

    public ObservableCollection<NavigationItem> MainNavigation { get; } = [];
    public ObservableCollection<NavigationItem> ExtraNavigation { get; } = [];
    public ObservableCollection<NavigationItem> ClinicalNavigation { get; } = [];
    public bool HasExtraNavigation => ExtraNavigation.Count > 0;
    public bool HasClinicalNavigation => ClinicalNavigation.Count > 0;

    [ObservableProperty]
    private bool _areFiltersOpen;
    [ObservableProperty]
    private bool _isExtraNavigationOpen;
    [ObservableProperty]
    private bool _isClinicalNavigationOpen;
    public string Greeting => $"Здравствуйте, {Session.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? Session.Name}";
    public Section? MainDestination => Session.Role switch
    {
        "Пациент" => Sections.FirstOrDefault(s => s.Key == "slots"),
        "Администратор" => Sections.FirstOrDefault(s => s.Key == "accounts"),
        _ => Sections.FirstOrDefault(s => s.Key == "appointments")};
    public string MainDestinationLabel => Session.Role switch
    {
        "Пациент" => "Записаться к врачу",
        "Врач" => "Открыть список приёмов",
        "Регистратор" => "Записи пациентов",
        _ => "Управление пользователями"
    };
    public bool HasMainDestination => MainDestination is not null;
    public string SearchPlaceholder => SelectedSection?.Key switch
    {
        "patients" => "Найти пациента по ФИО, карте или полису",
        "accounts" or "employees" => "Найти по имени или логину",
        "appointments" or "slots" or "schedule" => "Найти пациента, врача или кабинет",
        "services" or "referrals" => "Найти услугу или исследование",
        _ => "Поиск в этом разделе"
    };
    public bool HasStatusOptions => Statuses.Count > 2;
    public bool HasDateFilter => SelectedSection?.Key is "appointments" or "slots" or "schedule" or "visits" or "referrals" or "rendered" or "invoices" or "documents" or "notifications" or "reports" or "audit";
    public int ActiveFilterCount => (StatusFilter == "Все статусы" ? 0 : 1) + (FromDate.Length > 0 || ToDate.Length > 0 ? 1 : 0) + (SortOrder == "По умолчанию" ? 0 : 1);
    public string FilterLabel => ActiveFilterCount == 0 ? "Фильтры" : $"Фильтры · {ActiveFilterCount}";
    public string SelectionDescription => SelectedSection?.Key switch
    {
        "patients" => "Карта пациента",
        "appointments" => "Запись на приём",
        "accounts" => "Учётная запись",
        _ => SelectedSection?.Title ?? "Подробности"
    };

    [RelayCommand]
    private void ToggleFilters() => AreFiltersOpen = !AreFiltersOpen;
    [RelayCommand]
    private async Task OpenMainDestinationAsync()
    {
        if (Session.Role == "Пациент")
            await OpenEditorAsync("booking", 0, new());
        else if (MainDestination is not null)
            Navigate(MainDestination.Key);
    }

    private void InitializeNavigation()
    {
        string[] mainKeys = Session.Role switch
        {
            "Администратор" => ["home", "accounts", "employees", "services", "reports"],
            "Регистратор" => ["home", "appointments", "patients", "schedule", "invoices"],
            "Врач" => ["home", "appointments", "patients", "visits", "referrals"],
            _ => ["home", "appointments", "slots", "patients", "referrals"]
        };
        foreach (var key in mainKeys)
        {
            var section = Sections.FirstOrDefault(s => s.Key == key);
            if (section is not null)
                MainNavigation.Add(new(section));
        }

        foreach (var section in Sections.Where(s => !mainKeys.Contains(s.Key)))
        {
            var clinical = Session.Role == "Администратор" && section.Key is "patients" or "schedule" or "appointments" or "visits" or "referrals" or "invoices";
            (clinical ? ClinicalNavigation : ExtraNavigation).Add(new(section));
        }

        UpdateNavigation();
    }

    private void UpdateNavigation()
    {
        foreach (var item in MainNavigation.Concat(ExtraNavigation).Concat(ClinicalNavigation))
            item.IsActive = item.Key == SelectedSection?.Key;
        if (ExtraNavigation.Any(i => i.IsActive))
            IsExtraNavigationOpen = true;
        if (ClinicalNavigation.Any(i => i.IsActive))
            IsClinicalNavigationOpen = true;
        OnPropertyChanged(nameof(SearchPlaceholder));
        OnPropertyChanged(nameof(HasDateFilter));
        OnPropertyChanged(nameof(SelectionDescription));
        OnPropertyChanged(nameof(HasDayScope));
    }

    private void NotifyFilters()
    {
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(FilterLabel));
        OnPropertyChanged(nameof(HasStatusOptions));
        OnPropertyChanged(nameof(DateScopeLabel));
    }
}
