using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Clinic.Data;
using Clinic.Demo;
using Clinic.Workspace;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Npgsql;

namespace Clinic.ViewModels;

public partial class EditorField : ObservableObject
{
    public FieldSpec Spec { get; }
    public string Label => (IsDate ? Spec.Label.Replace(" (ГГГГ-ММ-ДД)", "") : Spec.Label) + (Spec.Required ? " *" : "");
    public bool IsChoice=>Spec.Lookup is not null;
    public bool IsDate => Spec.Key is "birth_date" or "work_date" or "valid_until";
    public bool IsText => !IsChoice && !IsDate;
    public bool IsReadOnly => Spec.Key == "file_name";
    public DateTime? Date
    {
        get => DateTime.TryParseExact(Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
        set => Text = value?.ToString("yyyy-MM-dd") ?? "";
    }
    partial void OnTextChanged(string value) => OnPropertyChanged(nameof(Date));
    public bool IsMultiline=>Spec.Multiline;
    public char PasswordChar=>Spec.Secret?'●':'\0';
    public string Placeholder => Spec.Key switch
    {
        "phone" => "+7 (999) 123-45-67",
        "oms_policy" => "16 цифр",
        "start_time" or "end_time" => "ЧЧ:ММ",
        "password" or "new_password" => "Не менее 8 символов",
        "file_name" => "Прикрепите файл кнопкой ниже",
        _ => ""
    };
    public ObservableCollection<Choice> Choices { get; }=[];
    [ObservableProperty] private string _text="";
    [ObservableProperty] private Choice? _selected;
    public string Value=>IsChoice?Selected?.Id??"":Text;
    public EditorField(FieldSpec spec,string value,IEnumerable<Choice>? choices=null)
    {
        Spec=spec;Text=value;
        if(choices is not null) foreach(var choice in choices)Choices.Add(choice);
        if(IsChoice)
        {
            if(!spec.Required)Choices.Insert(0,new Choice("","Не выбрано"));
            Selected=Choices.FirstOrDefault(c=>c.Id==value);
        }
    }
}
public sealed record RowAction(string Key,string Label);
public partial class WorkspaceViewModel : ViewModelBase
{
    public MainViewModel Owner { get; }
    public ClinicSession Session { get; }
    public IClinicGateway Gateway { get; }
    public ObservableCollection<Section> Sections { get; }=[];
    public IEnumerable<Section> QuickLinks => Sections.Where(s => s.Key != "home").Take(3);
    [RelayCommand] private void Navigate(string key)
    {
        if(CanInteract) SelectedSection=Sections.FirstOrDefault(s=>s.Key==key) ?? SelectedSection;
    }
    public ObservableCollection<Record> Rows { get; }=[];
    public ObservableCollection<DashboardMetric> Metrics { get; }=[];
    public ObservableCollection<DashboardItem> Events { get; }=[];
    public ObservableCollection<RowAction> Actions { get; }=[];
    public ObservableCollection<EditorField> Fields { get; }=[];
    public ObservableCollection<string> Statuses { get; }=["Все статусы"];
    public Func<string,Task>? FileAction { get; set; }
    private List<Record> _allRows=[];
    private string _editorKind="";
    private int _editorId;
    private byte[]? _attachedFile;
    private Dictionary<string,List<Choice>> _choices=[];
    private TaskCompletionSource<bool>? _restoreConfirmation;
    private int _loadVersion;
    private bool _loadFailed;
    private Dictionary<string, string> _editorValues = [];
    private TaskCompletionSource<bool>? _actionConfirmation;
    public ObservableCollection<string> SortOptions { get; } = ["По умолчанию", "По названию: А–Я", "По названию: Я–А", "Сначала новые", "Сначала старые"];
    [ObservableProperty] private string _sortOrder = "По умолчанию";
    [ObservableProperty] private bool _isActionConfirmOpen;
    [ObservableProperty] private string _confirmTitle = "";
    [ObservableProperty] private string _confirmDescription = "";
    [ObservableProperty] private string _confirmLabel = "Подтвердить";
    [ObservableProperty] private Section? _selectedSection;
    [ObservableProperty] private Record? _selectedRow;
    [ObservableProperty] private string _search="";
    [ObservableProperty] private string _statusFilter="Все статусы";
    [ObservableProperty] private string _fromDate="";
    [ObservableProperty] private string _toDate="";
    [ObservableProperty] private string _message="";
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isEditorOpen;
    [ObservableProperty] private bool _isRestoreConfirmOpen;
    [ObservableProperty] private string _restoreConfirmText="";
    [ObservableProperty] private string _editorTitle="";
    [ObservableProperty] private string _editorError="";
    [ObservableProperty] private string _eventTitle="";
    [ObservableProperty] private string _note="";
    public int? PatientFilter { get; private set; }
    public string Title=>SelectedSection?.Title??"Главная";
    public string Description=>SelectedSection?.Description??"";
    public bool IsHome=>SelectedSection?.Key=="home";
    public bool IsBackup=>SelectedSection?.Key=="backup";
    public bool IsTable=>!IsHome&&!IsBackup;
    public bool CanAdd=>SelectedSection?.NewLabel is not null;
    public string AddLabel=>SelectedSection?.NewLabel??"Добавить";
    public bool HasSelection=>SelectedRow is not null;
    public bool HasRows=>Rows.Count>0;
    public bool IsEmpty => IsTable && !HasRows && !IsBusy && !_loadFailed;
    public bool CanInteract => !IsBusy && !IsActionConfirmOpen && !IsRestoreConfirmOpen;
    public bool HasEvents => Events.Count > 0;
    public string EmptyTitle => _allRows.Count > 0 ? "Ничего не найдено" : "Пока нет записей";
    public string EmptyDescription => _allRows.Count > 0 ? "Измените поисковый запрос или сбросьте фильтры." : CanAdd ? "Добавьте первую запись с помощью кнопки выше." : "Записи появятся здесь по мере работы поликлиники.";
    public bool HasUnsavedChanges => IsEditorOpen && (_attachedFile is not null || Fields.Any(f => _editorValues.GetValueOrDefault(f.Spec.Key) != f.Value));
    public DateTime? FilterFrom { get => ParseDate(FromDate); set => FromDate = value?.ToString("yyyy-MM-dd") ?? ""; }
    public DateTime? FilterTo { get => ParseDate(ToDate); set => ToDate = value?.ToString("yyyy-MM-dd") ?? ""; }
    public bool HasMessage=>Message.Length>0;
    public bool CanAttach=>IsEditorOpen&&_editorKind=="result";
    public bool CanConfirmRestore=>RestoreConfirmText=="ВОССТАНОВИТЬ";
    public string BackupDescription=>Gateway is HttpClinicGateway?"Сохраните данные поликлиники, чтобы восстановить их при необходимости. Копия хранится в системе; на ваше устройство сохраняется файл для её восстановления.":"Копия содержит записи, файлы исследований и настройки пользователей.";
    public string RestoreDescription=>Gateway is HttpClinicGateway?"Восстановление заменит все текущие данные содержимым выбранной копии. Перед продолжением создайте свежую резервную копию.":"Значения из выбранной копии заменят текущие. Новые записи и журнал аудита сохранятся.";
    public string Header1=>SelectedSection?.Headers.ElementAtOrDefault(0)??"";
    public string Header2=>SelectedSection?.Headers.ElementAtOrDefault(1)??"";
    public string Header3=>SelectedSection?.Headers.ElementAtOrDefault(2)??"";
    public string Header4=>SelectedSection?.Headers.ElementAtOrDefault(3)??"";
    public string RowCount => Rows.Count == _allRows.Count ? CountLabel(Rows.Count) : $"Найдено {Rows.Count} из {_allRows.Count}";
    private static string CountLabel(int count) => count + " " + (count % 100 is >= 11 and <= 14 ? "записей" : (count % 10) switch { 1 => "запись", 2 or 3 or 4 => "записи", _ => "записей" });
    public WorkspaceViewModel(MainViewModel owner,IClinicGateway gateway,ClinicSession session)
    {
        Owner=owner;Gateway=gateway;Session=session;
        foreach(var section in Catalog.Sections(session.Role))Sections.Add(section);
        _selectedSection=Sections[0];
    }
    public async Task InitializeAsync()=>await RefreshAsync();
    partial void OnSelectedSectionChanged(Section? value)
    {
        _allRows = []; Rows.Clear(); _loadFailed = false;
        PatientFilter=null;SortOrder="По умолчанию";Search="";FromDate="";ToDate="";StatusFilter="Все статусы";SelectedRow=null;
        NotifyPage();_ = LoadPageAsync();
    }
    private void NotifyPage()
    {
        foreach(var p in new[]{nameof(Title),nameof(Description),nameof(IsHome),nameof(IsTable),nameof(IsBackup),nameof(CanAdd),nameof(AddLabel),nameof(Header1),nameof(Header2),nameof(Header3),nameof(Header4),nameof(EmptyTitle),nameof(EmptyDescription),nameof(HasRows),nameof(IsEmpty),nameof(RowCount),nameof(EmptyTitle),nameof(EmptyDescription)})OnPropertyChanged(p);
    }
    partial void OnSearchChanged(string value)=>Filter();
    partial void OnStatusFilterChanged(string value)=>Filter();
    partial void OnFromDateChanged(string value) { OnPropertyChanged(nameof(FilterFrom)); Filter(); }
    partial void OnToDateChanged(string value) { OnPropertyChanged(nameof(FilterTo)); Filter(); }
    partial void OnSortOrderChanged(string value) => Filter();
    partial void OnIsActionConfirmOpenChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
    partial void OnIsRestoreConfirmOpenChanged(bool value) => OnPropertyChanged(nameof(CanInteract));
    partial void OnMessageChanged(string value)=>OnPropertyChanged(nameof(HasMessage));
    partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(CanInteract)); }
    partial void OnIsEditorOpenChanged(bool value)=>OnPropertyChanged(nameof(CanAttach));
    partial void OnRestoreConfirmTextChanged(string value)=>OnPropertyChanged(nameof(CanConfirmRestore));
    partial void OnSelectedRowChanged(Record? value) {OnPropertyChanged(nameof(HasSelection));BuildActions();}
    [RelayCommand] private async Task RefreshAsync()=>await LoadPageAsync();
    private async Task LoadPageAsync()
    {
        var version=++_loadVersion;var section=SelectedSection;
        if(section is null)return;
        IsBusy=true;Message="";_loadFailed=false;
        try
        {
            if((FromDate.Length>0&&ParseDate(FromDate) is null)||(ToDate.Length>0&&ParseDate(ToDate) is null))
                throw new InvalidOperationException("Введите даты фильтра в формате ГГГГ-ММ-ДД.");
            if(ParseDate(FromDate) > ParseDate(ToDate)) throw new InvalidOperationException("Начало периода не может быть позже окончания.");
            if(section.Key=="home")
            {
                var dashboard=await Gateway.DashboardAsync(Session);if(version!=_loadVersion)return;
                Replace(Metrics,dashboard.Metrics);Replace(Events,dashboard.Items);EventTitle=dashboard.SectionTitle;Note=Catalog.WorkspaceNote(Session.Role);OnPropertyChanged(nameof(HasEvents));
            }
            else if(section.Key!="backup")
            {
                var records=await Gateway.ListAsync(Session,section.Key,ParseDate(FromDate),ParseDate(ToDate));if(version!=_loadVersion)return;
                var selectedId=SelectedRow?.Id;var selectedTable=SelectedRow?.Values.GetValueOrDefault("table");_allRows=records;
                var currentStatus=StatusFilter;Replace(Statuses,new[]{"Все статусы"}.Concat(records.Select(r=>r.Status).Distinct()));
                StatusFilter=Statuses.Contains(currentStatus)?currentStatus:"Все статусы";Filter();
                SelectedRow=Rows.FirstOrDefault(r=>r.Id==selectedId&&r.Values.GetValueOrDefault("table")==selectedTable);
            }
        }
        catch(Exception ex) {if(version==_loadVersion){_loadFailed=true;ShowError(Friendly(ex));}}
        finally {if(version==_loadVersion)IsBusy=false;}
    }
    private void Filter()
    {
        var search=Search.Trim();var from=ParseDate(FromDate);var to=ParseDate(ToDate);
        var rows=_allRows.Where(r=>
            (search.Length==0||string.Join(' ',r.Cell1,r.Cell2,r.Cell3,r.Cell4,r.Details,r.Values.GetValueOrDefault("oms_policy"),r.Values.GetValueOrDefault("card_number")).Contains(search,StringComparison.OrdinalIgnoreCase))&&
            (StatusFilter=="Все статусы"||r.Status==StatusFilter)&&
            (PatientFilter is null||(r.Values.GetValueOrDefault("patient_id")==PatientFilter.ToString()))&&
            (SelectedSection?.Key=="reports"||(from is null&&to is null)||MatchesDate(r,from,to)));
        rows = SortOrder switch
        {
            "По названию: А–Я" => rows.OrderBy(r => r.Cell1, StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), true)),
            "По названию: Я–А" => rows.OrderByDescending(r => r.Cell1, StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), true)),
            "Сначала новые" => rows.OrderByDescending(RecordDate).ThenByDescending(r => r.Id),
            "Сначала старые" => rows.OrderBy(RecordDate).ThenBy(r => r.Id),
            _ => rows
        };
        Replace(Rows,rows);if(SelectedRow is not null&&!Rows.Contains(SelectedRow))SelectedRow=null;
        foreach(var p in new[]{nameof(HasRows),nameof(IsEmpty),nameof(RowCount),nameof(EmptyTitle),nameof(EmptyDescription)})OnPropertyChanged(p);
    }
    private static DateTime? ParseDate(string text)=>DateTime.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var d)?d:null;
    private static DateTime? RecordDate(Record row)
    {
        var value = new[] { "work_date", "visited_at", "issued_at", "created_at", "rendered_at", "done_at", "acted_at", "birth_date", "last_login" }
            .Select(k => row.Values.GetValueOrDefault(k, "")).FirstOrDefault(v => v.Length > 0);
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, out var date)) return null;
        if (row.Values.ContainsKey("work_date") && TimeOnly.TryParse(row.Values.GetValueOrDefault("start_time", ""), out var time))
            return date.Date + time.ToTimeSpan();
        return date;
    }
    private static bool MatchesDate(Record row, DateTime? from, DateTime? to) =>
        RecordDate(row) is { } date && (from is null || date.Date >= from.Value.Date) && (to is null || date.Date <= to.Value.Date);
    private void BuildActions()
    {
        Actions.Clear();if(SelectedRow is null)return;
        var page=SelectedSection?.Key;var role=Session.Role;
        void Add(string key,string label)=>Actions.Add(new(key,label));
        if(page=="patients")
        {
            if(role=="Регистратор") {Add("edit","Изменить данные");Add("toggle_patient",SelectedRow.Status=="архив"?"Восстановить пациента":"Архивировать пациента");Add("new_document","Оформить документ");}
            if(role is "Пациент" or "Врач")Add("history","История посещений");
        }
        if(page=="slots")Add("book_slot","Записаться на это время");
        if(page=="appointments")
        {
            if(SelectedRow.Status=="запланирована")
            {
                if(role is "Пациент" or "Регистратор") {Add("reschedule","Перенести приём");Add("cancel","Отменить запись");}
                if(role=="Врач")Add("start_visit","Открыть приём");
                if(role is "Врач" or "Регистратор")Add("no_show","Отметить неявку");
            }
            Add("export","Сохранить талон");
        }
        if(page=="visits")
        {
            if(role=="Врач"&&SelectedRow.Status!="закрыт") {Add("edit","Редактировать протокол");Add("new_prescription","Назначить лечение");Add("new_referral","Выдать направление");Add("close_visit","Завершить приём");}
            Add("export","Сохранить протокол");
        }
        if(page=="referrals")
        {
            if(role=="Врач"&&SelectedRow.Status!="отменено") {if(Gateway is not HttpClinicGateway||SelectedRow.Status=="выдано")Add("result","Внести результат");if(SelectedRow.Status=="выдано")Add("cancel_referral","Отменить направление");}
            if(SelectedRow.Values.GetValueOrDefault("file_name","").Length>0)Add("download","Скачать файл");
            Add("export","Сохранить заключение");
        }
        if(page=="prescriptions") {if(role=="Врач"&&SelectedRow.Values.GetValueOrDefault("can_edit")!="false")Add("edit","Редактировать назначение");Add("export","Сохранить рецепт");}
        if(page=="schedule"&&role=="Регистратор")Add("edit","Изменить график");
        if(page=="rendered"&&SelectedRow.Status=="без счёта") {if(SelectedRow.Values.GetValueOrDefault("can_edit")!="false")Add("edit","Изменить услугу");Add("create_invoice","Сформировать счёт");}
        if(page=="invoices") {if(role=="Регистратор"&&SelectedRow.Status=="не оплачен")Add("payment","Принять оплату");Add("export","Сохранить счёт");}
        if(page=="notifications"&&SelectedRow.Status=="новое")Add("mark_read","Прочитано");
        if(page=="documents")Add("export","Сохранить документ");
        if(page is "accounts" or "employees" or "services") {Add("edit","Редактировать");Add("toggle_"+(page=="accounts"?"account":page=="employees"?"employee":"service"),page=="accounts"?(SelectedRow.Status=="активен"?"Заблокировать пользователя":"Разблокировать пользователя"):(SelectedRow.Status=="архив"?"Восстановить":"Архивировать"));}
        if(page=="dictionary")Add("edit","Редактировать");
    }
    [RelayCommand] private async Task AddAsync()
    {
        if(SelectedSection?.EditKind is string kind)await OpenEditorAsync(kind,0,new());
    }
    [RelayCommand] private async Task ChangePasswordAsync()=>await OpenEditorAsync("password",0,new());
    private async Task OpenEditorAsync(string kind,int id,Dictionary<string,string> values)
    {
        if(IsBusy)return;IsBusy=true;EditorError="";
        try
        {
            if(Gateway is HttpClinicGateway http)http.PrepareEditor(kind,id);
            _choices=kind=="password"?[]:await Gateway.ChoicesAsync(Session);Fields.Clear();_editorKind=kind;_editorId=id;_attachedFile=null;
            foreach(var spec in Gateway is HttpClinicGateway api?api.FieldsFor(kind,id):Catalog.Fields(kind))
            {
                if(Session.Role=="Пациент"&&spec.Key=="patient_id")values[spec.Key]=Session.PatientId?.ToString()??"";
                Fields.Add(new EditorField(spec,values.GetValueOrDefault(spec.Key,spec.Default??""),spec.Lookup is string key?_choices.GetValueOrDefault(key):null));
            }
            _editorValues = Fields.ToDictionary(f => f.Spec.Key, f => f.Value);
            EditorTitle=kind switch
            {
                "password"=>"Смена пароля", "booking"=>id==0?"Запись на приём":"Перенос записи",
                "result"=>"Результат исследования", "payment"=>"Приём оплаты", "visit"=>"Протокол осмотра",
                "patient"=>id==0?"Новый пациент":"Данные пациента", "prescription"=>id==0?"Назначение лечения":"Изменение назначения",
                "referral"=>"Направление на исследование", "rendered"=>"Оказанная услуга", "document"=>"Оформление документа",
                "account"=>id==0?"Новый пользователь":"Учётная запись", "employee"=>id==0?"Новый сотрудник":"Данные сотрудника",
                "service"=>id==0?"Новая услуга":"Услуга и стоимость", "schedule"=>id==0?"Новый график врача":"График врача",
                _=>id==0?"Новая запись справочника":"Запись справочника"
            };
            IsEditorOpen=true;
        }
        catch(Exception ex){ShowError(Friendly(ex));}finally{IsBusy=false;}
    }
    [RelayCommand] private async Task RowActionAsync(string key)
    {
        var row=SelectedRow;if(row is null||IsBusy||IsActionConfirmOpen)return;
        switch(key)
        {
            case "edit":await OpenEditorAsync(SelectedSection!.EditKind!,row.Id,new(row.Values));return;
            case "book_slot":await OpenEditorAsync("booking",0,new(row.Values));return;
            case "reschedule":await OpenEditorAsync("booking",row.Id,new(row.Values));return;
            case "result":await OpenEditorAsync("result",row.Id,new(row.Values));return;
            case "payment":
                var total=decimal.Parse(row.Values["total_text"],CultureInfo.InvariantCulture);var paid=decimal.Parse(row.Values["paid"],CultureInfo.InvariantCulture);
                await OpenEditorAsync("payment",row.Id,new(){["amount"]=(total-paid).ToString(CultureInfo.InvariantCulture)});return;
            case "new_prescription":await OpenEditorAsync("prescription",0,new(){["visit_id"]=row.Id.ToString()});return;
            case "new_referral":await OpenEditorAsync("referral",0,new(){["visit_id"]=row.Id.ToString()});return;
            case "new_document":await OpenEditorAsync("document",0,new(){["patient_id"]=row.Id.ToString()});return;
            case "history":
                var patient=row.Id;SelectedSection=Sections.First(s=>s.Key=="visits");await RefreshAsync();PatientFilter=patient;Filter();return;
            case "export":case "download":if(FileAction is not null)await FileAction(key);return;
        }
        var confirmation = key switch
        {
            "cancel" => ("Отменить запись?", "Выбранное время станет доступно для записи другим пациентам.", "Отменить запись"),
            "cancel_referral" => ("Отменить направление?", "По этому направлению больше нельзя будет внести результат исследования.", "Отменить направление"),
            "no_show" => ("Отметить неявку?", "Убедитесь, что пациент не пришёл на приём.", "Отметить неявку"),
            "close_visit" => ("Завершить приём?", "После завершения протокол приёма нельзя будет редактировать. Проверьте диагноз и рекомендации.", "Завершить приём"),
            "toggle_patient" when row.Status != "архив" => ("Архивировать пациента?", "Карта и история посещений сохранятся. Пациента можно восстановить из архива.", "Архивировать"),
            "toggle_account" when row.Status == "активен" => ("Заблокировать пользователя?", "Пользователь не сможет входить в систему до разблокировки учётной записи.", "Заблокировать"),
            "toggle_employee" or "toggle_service" when row.Status != "архив" => ("Архивировать запись?", "Запись сохранится в истории и станет недоступна при выборе в новых формах.", "Архивировать"),
            _ => ("", "", "")
        };
        if (confirmation.Item1.Length > 0 && !await ConfirmActionAsync(confirmation.Item1, confirmation.Item2 + "\n\n" + row.Cell1, confirmation.Item3)) return;
        IsBusy=true;
        try
        {
            await Gateway.ActAsync(Session,key,row.Id);
            if(key=="start_visit")
            {
                SelectedSection=Sections.First(s=>s.Key=="visits");await RefreshAsync();SelectedRow=Rows.FirstOrDefault(r=>r.Values.GetValueOrDefault("appointment_id")==row.Id.ToString());
                if(SelectedRow is not null) {IsBusy=false;await OpenEditorAsync("visit",SelectedRow.Id,new(SelectedRow.Values));}
            }
            else await RefreshAsync();
            ShowSuccess("Изменения сохранены.");
        }
        catch(Exception ex){ShowError(Friendly(ex));}finally{IsBusy=false;}
    }
    [RelayCommand] private async Task CloseEditorAsync()
    {
        if (IsBusy) return;
        if (HasUnsavedChanges && !await ConfirmDiscardAsync()) return;
        IsEditorOpen=false;Fields.Clear();_attachedFile=null;
    }
    public Task<bool> ConfirmDiscardAsync() => ConfirmActionAsync("Отменить изменения?", "Несохранённые изменения будут потеряны.", "Отменить изменения");
    private Task<bool> ConfirmActionAsync(string title, string description, string label)
    {
        if (_actionConfirmation is not null) return Task.FromResult(false);
        ConfirmTitle=title;ConfirmDescription=description;ConfirmLabel=label;
        _actionConfirmation=new(TaskCreationOptions.RunContinuationsAsynchronously);
        IsActionConfirmOpen=true;
        return _actionConfirmation.Task;
    }
    [RelayCommand] private void CancelAction()=>FinishActionConfirmation(false);
    [RelayCommand] private void ConfirmAction()=>FinishActionConfirmation(true);
    private void FinishActionConfirmation(bool confirmed)
    {
        IsActionConfirmOpen=false;var pending=_actionConfirmation;_actionConfirmation=null;pending?.TrySetResult(confirmed);
    }
    [RelayCommand] private void CloseDetails()=>SelectedRow=null;
    [RelayCommand] private void DismissMessage()=>Message="";
    [RelayCommand] private async Task ApplyFiltersAsync()=>await RefreshAsync();
    [RelayCommand] private async Task SaveAsync()
    {
        if(IsBusy)return;IsBusy=true;EditorError="";
        try
        {
            FormValidation.Validate(Fields);
            var values=Fields.ToDictionary(f=>f.Spec.Key,f=>f.Value);
            if(_attachedFile is not null)values["file_data"]=Convert.ToBase64String(_attachedFile);
            await Gateway.SaveAsync(Session,new(_editorKind,_editorId,values));
            IsEditorOpen=false;Fields.Clear();_attachedFile=null;await RefreshAsync();ShowSuccess("Изменения сохранены.");
        }
        catch(Exception ex){EditorError=Friendly(ex);}finally{IsBusy=false;}
    }
    public void Attach(string name,byte[] data)
    {
        if(data.Length>10*1024*1024){EditorError="Максимальный размер файла — 10 МБ.";return;}
        EditorError="";_attachedFile=data;var field=Fields.FirstOrDefault(f=>f.Spec.Key=="file_name");if(field is not null)field.Text=name;
    }
    [RelayCommand] private async Task FileAsync(string kind) {if(!IsBusy&&FileAction is not null)await FileAction(kind);}
    public Task<bool> ConfirmRestoreAsync()
    {
        if(_restoreConfirmation is not null)return Task.FromResult(false);
        RestoreConfirmText="";IsRestoreConfirmOpen=true;
        _restoreConfirmation=new(TaskCreationOptions.RunContinuationsAsynchronously);
        return _restoreConfirmation.Task;
    }
    [RelayCommand] private void CancelRestore()=>FinishRestoreConfirmation(false);
    [RelayCommand] private void ConfirmRestore() {if(CanConfirmRestore)FinishRestoreConfirmation(true);}
    private void FinishRestoreConfirmation(bool confirmed)
    {
        IsRestoreConfirmOpen=false;var pending=_restoreConfirmation;_restoreConfirmation=null;pending?.TrySetResult(confirmed);
    }
    [RelayCommand] private void ResetFilters() {Search="";StatusFilter="Все статусы";FromDate="";ToDate="";PatientFilter=null;Filter();if(SelectedSection?.Key=="reports")_ = RefreshAsync();}
    public string ExportHtml(bool all=false)
    {
        string E(string s)=>System.Net.WebUtility.HtmlEncode(s);
        var title=all?Title:SelectedRow?.Cell1??Title;
        var body=all?"<table><tr>"+string.Join("",new[]{Header1,Header2,Header3,Header4,"Статус"}.Select(h=>"<th>"+E(h)+"</th>"))+"</tr>"+string.Join("",Rows.Select(r=>"<tr>"+string.Join("",new[]{r.Cell1,r.Cell2,r.Cell3,r.Cell4,r.Status}.Select(x=>"<td>"+E(x)+"</td>"))+"</tr>"))+"</table>":"<p>"+E(SelectedRow?.Cell2??"")+" · "+E(SelectedRow?.Cell3??"")+" · "+E(SelectedRow?.Cell4??"")+"</p><p>Статус: "+E(SelectedRow?.Status??"")+"</p><pre>"+E(SelectedRow?.Details??"")+"</pre>";
        return "<!doctype html><html lang='ru'><meta charset='utf-8'><title>"+E(title)+"</title><style>body{font:16px system-ui;max-width:960px;margin:48px auto;color:#172c43}h1{font-size:26px}pre{white-space:pre-wrap;font:inherit;line-height:1.6}table{width:100%;border-collapse:collapse}td,th{text-align:left;padding:12px;border:1px solid #dfe9f1}button{padding:10px 18px}@media print{button{display:none}body{margin:20px}}</style><h1>Медицинская информационная система</h1><h2>"+E(title)+"</h2>"+body+"<p>Сформировано: "+DateTime.Now.ToString("dd.MM.yyyy HH:mm")+"</p><button onclick='window.print()'>Печать / сохранить PDF</button></html>";
    }
    public void ShowSuccess(string text){IsError=false;Message=text;}
    public void ShowError(string text){IsError=true;Message=text;}
    public static string Friendly(Exception exception)=>exception switch
    {
        PostgresException {SqlState:"23505"}=>"Такая запись уже существует или слот занят. Обновите список.",
        PostgresException {SqlState:"23503"}=>"Выбранная запись связана с другими данными. Проверьте поля.",
        PostgresException {SqlState:"42501"}=>"Недостаточно прав для этого действия. Обратитесь к администратору.",
        PostgresException {SqlState:"22001"}=>"Одно из полей превышает допустимую длину.",
        NpgsqlException=>"Не удалось подключиться к поликлинике. Повторите попытку.",
        InvalidOperationException=>exception.Message,
        _=>"Не удалось выполнить действие. Проверьте данные и повторите попытку."
    };
    private static void Replace<T>(ObservableCollection<T> target,IEnumerable<T> source) {target.Clear();foreach(var item in source)target.Add(item);}
}
