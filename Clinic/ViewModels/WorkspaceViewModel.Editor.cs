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
    private async Task AddAsync()
    {
        if (SelectedSection?.EditKind is string kind)
            await OpenEditorAsync(kind, 0, new());
    }

    [RelayCommand]
    private async Task ChangePasswordAsync() => await OpenEditorAsync("password", 0, new());
    private async Task OpenEditorAsync(string kind, int id, Dictionary<string, string> values)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        EditorError = "";
        try
        {
            if (Gateway is HttpClinicGateway http)
                http.PrepareEditor(kind, id);
            _choices = kind == "password" ? [] : await Gateway.ChoicesAsync(Session);
            Fields.Clear();
            _editorKind = kind;
            _editorId = id;
            _attachedFile = null;
            _attachedFiles.Clear();
            var specifications = Gateway is HttpClinicGateway api ? api.FieldsFor(kind, id) : Catalog.Fields(kind);
            if (kind == "booking" && Gateway is HttpClinicGateway)
                specifications = specifications.Where(f => f.Key != "slot").Concat([new FieldSpec("booking_doctor", "Врач", true, "doctors"), new FieldSpec("booking_date", "Дата", true), new FieldSpec("slot", "Время приёма", true, "slots")]).ToArray();
            foreach (var spec in specifications)
            {
                if (Session.Role == "Пациент" && spec.Key == "patient_id")
                    values[spec.Key] = Session.PatientId?.ToString() ?? "";
                Fields.Add(new EditorField(spec, values.GetValueOrDefault(spec.Key, spec.Default ?? ""), spec.Lookup is string key ? _choices.GetValueOrDefault(key) : null));
            }

            if (kind == "booking" && Gateway is HttpClinicGateway bookingApi)
                ConfigureBooking(bookingApi, values);
            _editorValues = Fields.ToDictionary(f => f.Spec.Key, f => f.Value);
            EditorTitle = kind switch
            {
                "role_permissions" => "Права роли",
                "password" => "Смена пароля",
                "booking" => id == 0 ? "Запись на приём" : "Перенос записи",
                "result" => "Результат исследования",
                "payment" => "Приём оплаты",
                "visit" => "Протокол осмотра",
                "patient" => id == 0 ? "Новый пациент" : "Данные пациента",
                "prescription" => id == 0 ? "Назначение лечения" : "Изменение назначения",
                "referral" => "Направление на исследование",
                "rendered" => "Оказанная услуга",
                "document" => "Оформление документа",
                "account" => id == 0 ? "Новый пользователь" : "Учётная запись",
                "employee" => id == 0 ? "Новый сотрудник" : "Данные сотрудника",
                "service" => id == 0 ? "Новая услуга" : "Услуга и стоимость",
                "schedule" => id == 0 ? "Новый график врача" : "График врача",
                _ => id == 0 ? "Новая запись справочника" : "Запись справочника"
            };
            IsEditorOpen = true;
        }
        catch (Exception ex)
        {
            ShowError(Friendly(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CloseEditorAsync()
    {
        if (IsBusy)
            return;
        if (HasUnsavedChanges && !await ConfirmDiscardAsync())
            return;
        IsEditorOpen = false;
        Fields.Clear();
        _attachedFile = null;
        _attachedFiles.Clear();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        EditorError = "";
        try
        {
            FormValidation.Validate(Fields);
            var values = Fields.ToDictionary(f => f.Spec.Key, f => f.Value);
            if (_attachedFile is not null)
                values["file_data"] = Convert.ToBase64String(_attachedFile);
            if (Gateway is HttpClinicGateway && _attachedFiles.Count > 0)
                values["files_json"] = System.Text.Json.JsonSerializer.Serialize(_attachedFiles);
            await Gateway.SaveAsync(Session, new(_editorKind, _editorId, values));
            IsEditorOpen = false;
            Fields.Clear();
            _attachedFile = null;
            _attachedFiles.Clear();
            await RefreshAsync();
            ShowSuccess("Изменения сохранены.");
        }
        catch (Exception ex)
        {
            EditorError = Friendly(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public bool Attach(string name, byte[] data)
    {
        if (_attachedFiles.Count >= 10)
        {
            EditorError = "Можно прикрепить до 10 файлов.";
            return false;
        }

        if (data.Length == 0)
        {
            EditorError = "Файл пустой.";
            return false;
        }

        if (_attachedFiles.Sum(f => Convert.FromBase64String(f.Data).Length) + data.Length > 10 * 1024 * 1024)
        {
            EditorError = "Общий размер файлов — не более 10 МБ.";
            return false;
        }

        var extension = System.IO.Path.GetExtension(name).ToLowerInvariant();
        if (extension is not (".pdf" or ".jpg" or ".jpeg" or ".png"))
        {
            EditorError = "Допустимы PDF, JPG и PNG.";
            return false;
        }

        EditorError = "";
        _attachedFile = data;
        _attachedFiles.Add(new(name, Convert.ToBase64String(data)));
        var field = Fields.FirstOrDefault(f => f.Spec.Key == "file_name");
        if (field is not null)
            field.Text = string.Join(", ", _attachedFiles.Select(f => f.Name));
        return true;
    }

    [RelayCommand]
    private async Task FileAsync(string kind)
    {
        if (!IsBusy && FileAction is not null)
            await FileAction(kind);
    }
}
