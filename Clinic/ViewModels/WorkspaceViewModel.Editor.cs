using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Clinic.Data;
using Clinic.Workspace;
using CommunityToolkit.Mvvm.Input;

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
    private Task ChangePasswordAsync()
    {
        return OpenEditorAsync("password", 0, new());
    }
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
            if (kind == "password")
                _choices = [];
            else
                _choices = await Gateway.ChoicesAsync(Session);
            Fields.Clear();
            _editorKind = kind;
            _editorId = id;
            _attachedFile = null;
            _attachedFiles.Clear();
            var specifications = Gateway is HttpClinicGateway api ? api.FieldsFor(kind, id) : Catalog.Fields(kind);
            if (kind == "booking" && Gateway is HttpClinicGateway)
            {
                specifications = specifications.Where(field => field.Key != "slot").Concat([
                    new FieldSpec("booking_doctor", "Врач", true, "doctors"),
                    new FieldSpec("booking_date", "Дата", true),
                    new FieldSpec("slot", "Время приёма", true, "slots")
                ]).ToArray();
            }
            foreach (var spec in specifications)
            {
                if (Session.Role == "Пациент" && spec.Key == "patient_id")
                    values[spec.Key] = Session.PatientId?.ToString() ?? "";
                var value = values.GetValueOrDefault(spec.Key, spec.Default ?? "");
                var choices = spec.Lookup is string lookup ? _choices.GetValueOrDefault(lookup) : null;
                Fields.Add(new EditorField(spec, value, choices));
            }

            if (kind == "booking" && Gateway is HttpClinicGateway bookingApi)
                ConfigureBooking(bookingApi, values);
            if (kind == "employee")
                ConfigureEmployee();
            if (kind == "account" && id == 0)
                ConfigureAccount();
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
        ClearEditor();
    }

    private void ClearEditor()
    {
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
            ClearEditor();
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
        if (IsBusy)
            return;
        if (FileAction is null)
        {
            ShowError("Не удалось открыть сохранение файла. Откройте раздел заново.");
            return;
        }
        await FileAction(kind);
    }

    private void ConfigureEmployee()
    {
        var postField = Fields.FirstOrDefault(f => f.Spec.Key == "post_id");
        var specialtyField = Fields.FirstOrDefault(f => f.Spec.Key == "specialty_id");
        if (postField is null || specialtyField is null)
            return;

        var specialties = _choices.GetValueOrDefault("specialties") ?? [];

        void UpdateSpecialty()
        {
            var isDoctor = postField.Selected?.Name.Contains("врач", StringComparison.OrdinalIgnoreCase) == true;
            specialtyField.IsVisible = isDoctor;
            specialtyField.IsRequired = isDoctor;
            if (isDoctor)
            {
                var current = specialtyField.Value;
                Replace(specialtyField.Choices, specialties);
                specialtyField.Selected = specialtyField.Choices.FirstOrDefault(c => c.Id == current);
            }
            else
            {
                specialtyField.Selected = null;
            }
        }

        postField.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EditorField.Selected))
                UpdateSpecialty();
        };

        UpdateSpecialty();
    }

    private void ConfigureAccount()
    {
        var roleField = Fields.FirstOrDefault(f => f.Spec.Key == "role_id");
        var employeeField = Fields.FirstOrDefault(f => f.Spec.Key == "employee_id");
        var patientField = Fields.FirstOrDefault(f => f.Spec.Key == "patient_id");
        if (roleField is null || employeeField is null || patientField is null)
            return;

        var freeEmployees = _choices.GetValueOrDefault("employees") ?? [];
        var freeDoctors = _choices.GetValueOrDefault("free_doctors") ?? freeEmployees;
        var freePatients = _choices.GetValueOrDefault("patients") ?? [];

        void UpdateOwner()
        {
            var roleName = roleField.Selected?.Name ?? "";
            var hasRole = roleName.Length > 0;
            var isPatient = string.Equals(roleName, "Пациент", StringComparison.OrdinalIgnoreCase);
            var isDoctor = roleName.Contains("врач", StringComparison.OrdinalIgnoreCase);

            patientField.IsVisible = hasRole && isPatient;
            patientField.IsRequired = hasRole && isPatient;
            employeeField.IsVisible = hasRole && !isPatient;
            employeeField.IsRequired = hasRole && !isPatient;

            if (patientField.IsVisible)
            {
                var current = patientField.Value;
                patientField.EmptyChoicePlaceholder = "Нет пациентов без учётной записи";
                Replace(patientField.Choices, freePatients);
                patientField.Selected = patientField.Choices.FirstOrDefault(c => c.Id == current);
            }
            else
            {
                patientField.Selected = null;
            }

            if (employeeField.IsVisible)
            {
                var current = employeeField.Value;
                employeeField.EmptyChoicePlaceholder = isDoctor
                    ? "Нет врачей без аккаунта — добавьте врача в разделе «Сотрудники»"
                    : "Нет сотрудников без аккаунта — добавьте в разделе «Сотрудники»";
                Replace(employeeField.Choices, isDoctor ? freeDoctors : freeEmployees);
                employeeField.Selected = employeeField.Choices.FirstOrDefault(c => c.Id == current);
            }
            else
            {
                employeeField.Selected = null;
            }
        }

        roleField.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EditorField.Selected))
                UpdateOwner();
        };

        UpdateOwner();
    }
}
