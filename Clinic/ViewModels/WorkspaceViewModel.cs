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

public sealed record RowAction(string Key, string Label)
{
    public bool IsPrimary => Key is "edit" or "start_visit" or "book_slot" or "result" or "payment" or "create_invoice";
}

// Состояние рабочего стола и переходы между разделами.
// Загрузка списков — в Data, формы — в Editor, действия с записью — в Actions.
public partial class WorkspaceViewModel : ViewModelBase
{
    public MainViewModel Owner { get; }
    public ClinicSession Session { get; }
    public IClinicGateway Gateway { get; }
    public ObservableCollection<Section> Sections { get; } = [];

    [RelayCommand]
    private void Navigate(string key)
    {
        if (CanInteract)
            SelectedSection = Sections.FirstOrDefault(s => s.Key == key) ?? SelectedSection;
    }

    public ObservableCollection<Record> Rows { get; } = [];
    public ObservableCollection<ReportChart> ReportCharts { get; } = [];

    [ObservableProperty]
    private bool _reportTable;
    public bool ShowReportCharts => IsReports && !ReportTable;
    public bool ShowRecords => !IsReports || ReportTable;
    public string ReportDisplayLabel => ReportTable ? "Диаграммы" : "Таблица";

    [RelayCommand]
    private void ToggleReportDisplay() => ReportTable = !ReportTable;
    partial void OnReportTableChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowReportCharts));
        OnPropertyChanged(nameof(ShowRecords));
        OnPropertyChanged(nameof(ReportDisplayLabel));
    }

    public bool IsReports => SelectedSection?.Key == "reports";
    public ObservableCollection<DashboardMetric> Metrics { get; } = [];
    public ObservableCollection<DashboardItem> Events { get; } = [];
    public ObservableCollection<RowAction> Actions { get; } = [];
    public ObservableCollection<EditorField> Fields { get; } = [];
    public ObservableCollection<string> Statuses { get; } = ["Все статусы"];
    public Func<string, Task>? FileAction { get; set; }

    private List<Record> _allRows = [];
    private string _editorKind = "";
    private int _editorId;
    private byte[]? _attachedFile;
    private readonly List<AttachmentUpload> _attachedFiles = [];
    private TaskCompletionSource<AttachmentInfo?>? _attachmentChoice;
    public ObservableCollection<AttachmentInfo> AttachmentChoices { get; } = [];

    [ObservableProperty]
    private bool _isAttachmentPickerOpen;
    public bool CanDeleteAttachment => Session.Role == "Врач" && (Gateway is not HttpClinicGateway api || api.Permissions.Contains("referral.result"));

    private Dictionary<string, List<Choice>> _choices = [];
    private TaskCompletionSource<bool>? _restoreConfirmation;
    private int _loadVersion;
    private bool _loadFailed;
    private Dictionary<string, string> _editorValues = [];
    private TaskCompletionSource<bool>? _actionConfirmation;
    public ObservableCollection<string> SortOptions { get; } = ["По умолчанию", "По названию: А–Я", "По названию: Я–А", "Сначала новые", "Сначала старые"];

    [ObservableProperty]
    private string _sortOrder = "По умолчанию";
    [ObservableProperty]
    private bool _isActionConfirmOpen;
    [ObservableProperty]
    private string _confirmTitle = "";
    [ObservableProperty]
    private string _confirmDescription = "";
    [ObservableProperty]
    private string _confirmLabel = "Подтвердить";
    [ObservableProperty]
    private Section? _selectedSection;
    [ObservableProperty]
    private Record? _selectedRow;
    [ObservableProperty]
    private string _search = "";
    [ObservableProperty]
    private string _statusFilter = "Все статусы";
    [ObservableProperty]
    private string _fromDate = "";
    [ObservableProperty]
    private string _toDate = "";
    [ObservableProperty]
    private string _message = "";
    [ObservableProperty]
    private bool _isError;
    [ObservableProperty]
    private bool _isBusy;
    [ObservableProperty]
    private bool _isEditorOpen;
    [ObservableProperty]
    private bool _isRestoreConfirmOpen;
    [ObservableProperty]
    private string _restoreConfirmText = "";
    [ObservableProperty]
    private string _editorTitle = "";
    [ObservableProperty]
    private string _editorError = "";
    [ObservableProperty]
    private string _eventTitle = "";
    [ObservableProperty]
    private string _note = "";
    public int? PatientFilter { get; private set; }
    public string Title => SelectedSection?.Title ?? "Главная";
    public string Description => SelectedSection?.Description ?? "";
    public bool IsHome => SelectedSection?.Key == "home";
    public bool IsBackup => SelectedSection?.Key == "backup";
    public bool IsTable => !IsHome && !IsBackup;
    public bool CanAdd => SelectedSection?.NewLabel is not null && (Gateway is not HttpClinicGateway api || AccessPresentation.Write(Session.Role, SelectedSection.EditKind, api.Permissions));
    public string AddLabel => SelectedSection?.NewLabel ?? "Добавить";
    public bool HasSelection => SelectedRow is not null;
    public bool HasRows => Rows.Count > 0;
    public bool IsEmpty => IsTable && !HasRows && !IsBusy && !_loadFailed;
    public bool CanInteract => !IsBusy && !IsActionConfirmOpen && !IsRestoreConfirmOpen && !IsAttachmentPickerOpen;
    public bool HasEvents => Events.Count > 0;
    public string EmptyTitle => _allRows.Count > 0 ? "Ничего не найдено" : "Пока нет записей";
    public string EmptyDescription => _allRows.Count > 0 ? "Измените поисковый запрос или сбросьте фильтры." : CanAdd ? "Добавьте первую запись с помощью кнопки выше." : "Записи появятся здесь по мере работы поликлиники.";
    public bool HasUnsavedChanges => IsEditorOpen && (_attachedFile is not null || Fields.Any(f => _editorValues.GetValueOrDefault(f.Spec.Key) != f.Value));
    public DateTime? FilterFrom { get => ParseDate(FromDate); set => FromDate = value?.ToString("yyyy-MM-dd") ?? ""; }
    public DateTime? FilterTo { get => ParseDate(ToDate); set => ToDate = value?.ToString("yyyy-MM-dd") ?? ""; }
    public bool HasMessage => Message.Length > 0;
    public string EditorSaveLabel => _editorKind == "booking" ? (_editorId == 0 ? "Записаться" : "Перенести приём") : "Сохранить";
    public bool CanAttach => IsEditorOpen && _editorKind == "result";
    public bool CanConfirmRestore => RestoreConfirmText == "ВОССТАНОВИТЬ";
    public string BackupDescription => Gateway is HttpClinicGateway ? "Сохраните данные поликлиники, чтобы восстановить их при необходимости. Копия хранится в системе; на ваше устройство сохраняется файл для её восстановления." : "Копия содержит записи, файлы исследований и настройки пользователей.";
    public string RestoreDescription => Gateway is HttpClinicGateway ? "Восстановление заменит все текущие данные содержимым выбранной копии. Перед продолжением создайте свежую резервную копию." : "Значения из выбранной копии заменят текущие. Новые записи и журнал аудита сохранятся.";
    public string Header1 => SelectedSection?.Headers.ElementAtOrDefault(0) ?? "";
    public string Header2 => SelectedSection?.Headers.ElementAtOrDefault(1) ?? "";
    public string Header3 => SelectedSection?.Headers.ElementAtOrDefault(2) ?? "";
    public string Header4 => SelectedSection?.Headers.ElementAtOrDefault(3) ?? "";
    public string RowCount => Rows.Count == _allRows.Count ? CountLabel(Rows.Count) : $"Найдено {Rows.Count} из {_allRows.Count}";

    private static string CountLabel(int count) => count + " " + (count % 100 is >= 11 and <= 14 ? "записей" : (count % 10) switch
    {
        1 => "запись",
        2 or 3 or 4 => "записи",
        _ => "записей"
    });
    public WorkspaceViewModel(MainViewModel owner, IClinicGateway gateway, ClinicSession session)
    {
        Owner = owner;
        Gateway = gateway;
        Session = session;
        foreach (var section in Catalog.Sections(session.Role))
            if (gateway is not HttpClinicGateway api || AccessPresentation.Read(session.Role, section.Key, api.Permissions))
                Sections.Add(section);
        if (gateway is HttpClinicGateway http && http.Permissions.Contains("role.manage"))
            Sections.Add(new("roles", "Роли и права", "♙", "Доступ сотрудников к разделам", ["Роль", "Название", "Разрешения", ""], "role_permissions"));
        _selectedSection = Sections[0];
        InitializeNavigation();
    }

    public async Task InitializeAsync() => await RefreshAsync();
    private bool _navigationApproved;
    partial void OnSelectedSectionChanged(Section? oldValue, Section? newValue)
    {
        var value = newValue;
        if (HasUnsavedChanges && !_navigationApproved)
        {
            _selectedSection = oldValue;
            OnPropertyChanged(nameof(SelectedSection));
            _ = ConfirmNavigationAsync(value);
            return;
        }

        _allRows = [];
        Rows.Clear();
        _loadFailed = false;
        PatientFilter = null;
        SortOrder = "По умолчанию";
        Search = "";
        FromDate = "";
        ToDate = "";
        StatusFilter = "Все статусы";
        if (SelectedSection?.Key == "slots" || (SelectedSection?.Key == "appointments" && Session.Role is "Врач" or "Регистратор"))
        {
            FromDate = DateTime.Today.ToString("yyyy-MM-dd");
            ToDate = FromDate;
        }

        SelectedRow = null;
        AreFiltersOpen = false;
        UpdateNavigation();
        NotifyPage();
        _ = LoadPageAsync();
    }

    private async Task ConfirmNavigationAsync(Section? target)
    {
        if (!await ConfirmDiscardAsync())
            return;
        IsEditorOpen = false;
        Fields.Clear();
        _attachedFile = null;
        _attachedFiles.Clear();
        _navigationApproved = true;
        try
        {
            SelectedSection = target;
        }
        finally
        {
            _navigationApproved = false;
        }
    }

    private void NotifyPage()
    {
        foreach (var p in new[]
        {
            nameof(Title),
            nameof(IsReports),
            nameof(ShowReportCharts),
            nameof(ShowRecords),
            nameof(Description),
            nameof(IsHome),
            nameof(IsTable),
            nameof(IsBackup),
            nameof(CanAdd),
            nameof(AddLabel),
            nameof(Header1),
            nameof(Header2),
            nameof(Header3),
            nameof(Header4),
            nameof(EmptyTitle),
            nameof(EmptyDescription),
            nameof(HasRows),
            nameof(IsEmpty),
            nameof(RowCount),
            nameof(EmptyTitle),
            nameof(EmptyDescription)
        }

        )
            OnPropertyChanged(p);
    }

    partial void OnSearchChanged(string value) => Filter();
    partial void OnStatusFilterChanged(string value) => Filter();
    partial void OnFromDateChanged(string value)
    {
        OnPropertyChanged(nameof(FilterFrom));
        Filter();
    }

    partial void OnToDateChanged(string value)
    {
        OnPropertyChanged(nameof(FilterTo));
        Filter();
    }

    partial void OnSortOrderChanged(string value) => Filter();
    partial void OnIsAttachmentPickerOpenChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
    partial void OnIsActionConfirmOpenChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
    partial void OnIsRestoreConfirmOpenChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
    partial void OnMessageChanged(string value) => OnPropertyChanged(nameof(HasMessage));
    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanInteract));
    }

    partial void OnIsEditorOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAttach));
        OnPropertyChanged(nameof(EditorSaveLabel));
    }
    partial void OnRestoreConfirmTextChanged(string value) => OnPropertyChanged(nameof(CanConfirmRestore));
    partial void OnSelectedRowChanged(Record? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        BuildActions();
    }

    [RelayCommand]
    private void CloseDetails() => SelectedRow = null;
    [RelayCommand]
    private void DismissMessage() => Message = "";
    public void ShowSuccess(string text)
    {
        IsError = false;
        Message = text;
    }

    public void ShowError(string text)
    {
        IsError = true;
        Message = text;
    }

    public static string Friendly(Exception exception) => exception switch
    {
        PostgresException { SqlState: "23505" } => "Такая запись уже существует или слот занят. Обновите список.",
        PostgresException { SqlState: "23503" } => "Выбранная запись связана с другими данными. Проверьте поля.",
        PostgresException { SqlState: "42501" } => "Недостаточно прав для этого действия. Обратитесь к администратору.",
        PostgresException { SqlState: "22001" } => "Одно из полей превышает допустимую длину.",
        NpgsqlException => "Не удалось подключиться к поликлинике. Повторите попытку.",
        InvalidOperationException => exception.Message,
        _ => "Не удалось выполнить действие. Проверьте данные и повторите попытку."
    };
    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }
}
