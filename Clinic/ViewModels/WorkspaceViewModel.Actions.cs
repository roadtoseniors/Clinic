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
    private void BuildActions()
    {
        Actions.Clear();
        if (SelectedRow is null)
            return;
        var page = SelectedSection?.Key;
        var role = Session.Role;
        void Add(string key, string label) => Actions.Add(new(key, label));
        if (page == "patients")
        {
            if (role == "Регистратор")
            {
                Add("edit", "Изменить данные");
                Add("toggle_patient", SelectedRow.Status == "архив" ? "Восстановить пациента" : "Архивировать пациента");
                Add("new_document", "Оформить документ");
            }

            if (role is "Пациент" or "Врач")
                Add("history", "История посещений");
        }

        if (page == "slots")
            Add("book_slot", "Записаться на это время");
        if (page == "appointments")
        {
            if (SelectedRow.Status == "запланирована")
            {
                if (role is "Пациент" or "Регистратор")
                {
                    Add("reschedule", "Перенести приём");
                    Add("cancel", "Отменить запись");
                }

                if (role == "Врач")
                    Add("start_visit", "Открыть приём");
                if (role is "Врач" or "Регистратор")
                    Add("no_show", "Отметить неявку");
            }

            Add("export", "Сохранить талон");
        }

        if (page == "visits")
        {
            if (role == "Врач" && SelectedRow.Status != "закрыт")
            {
                Add("edit", "Редактировать протокол");
                Add("new_prescription", "Назначить лечение");
                Add("new_referral", "Выдать направление");
                Add("close_visit", "Завершить приём");
            }

            Add("export", "Сохранить протокол");
        }

        if (page == "referrals")
        {
            if (role == "Врач" && SelectedRow.Status != "отменено")
            {
                if (Gateway is not HttpClinicGateway || SelectedRow.Status == "выдано")
                    Add("result", "Внести результат");
                if (SelectedRow.Status == "выдано")
                    Add("cancel_referral", "Отменить направление");
            }

            if (SelectedRow.Values.GetValueOrDefault("file_name", "").Length > 0)
                Add("download", "Скачать файл");
            Add("export", "Сохранить заключение");
        }

        if (page == "prescriptions")
        {
            if (role == "Врач" && SelectedRow.Values.GetValueOrDefault("can_edit") != "false")
                Add("edit", "Редактировать назначение");
            Add("export", "Сохранить рецепт");
        }

        if (page == "schedule" && role == "Регистратор")
            Add("edit", "Изменить график");
        if (page == "rendered" && SelectedRow.Status == "без счёта")
        {
            if (SelectedRow.Values.GetValueOrDefault("can_edit") != "false")
                Add("edit", "Изменить услугу");
            Add("create_invoice", "Сформировать счёт");
        }

        if (page == "invoices")
        {
            if (role == "Регистратор" && SelectedRow.Status == "не оплачен")
                Add("payment", "Принять оплату");
            Add("export", "Сохранить счёт");
        }

        if (page == "notifications")
        {
            if (SelectedRow.Status == "новое")
                Add("mark_read", "Прочитано");
            if (Gateway is HttpClinicGateway)
                Add("read_all", "Прочитать все");
        }

        if (page == "documents")
            Add("export", "Сохранить документ");
        if (page is "accounts" or "employees" or "services")
        {
            Add("edit", "Редактировать");
            Add("toggle_" + (page == "accounts" ? "account" : page == "employees" ? "employee" : "service"), page == "accounts" ? (SelectedRow.Status == "активен" ? "Заблокировать пользователя" : "Разблокировать пользователя") : (SelectedRow.Status == "архив" ? "Восстановить" : "Архивировать"));
        }

        if (page == "roles")
            Add("edit", "Изменить права");
        if (page == "dictionary")
            Add("edit", "Редактировать");
        if (Gateway is HttpClinicGateway)
        {
            if (page == "referrals" && role == "Врач" && SelectedRow.Status != "выполнено")
                Add("delete_referral", "Удалить неисполненное направление");
            if (page == "schedule" && role == "Регистратор")
                Add("delete_schedule", "Удалить пустой график");
            if (page == "prescriptions" && role == "Врач" && SelectedRow.Values.GetValueOrDefault("can_delete") == "true")
                Add("delete_prescription", "Удалить назначение");
            if (page == "rendered" && SelectedRow.Status == "без счёта")
                Add("delete_rendered", "Удалить начисление");
            if (page == "invoices" && role == "Регистратор" && SelectedRow.Status == "не оплачен")
                Add("cancel_invoice", "Отменить счёт");
            if (page == "dictionary")
                Add("delete_dictionary", "Удалить запись");
            if (page == "accounts")
                Add("edit_permissions", "Права выбранной роли");
            if (page == "accounts")
                Add("delete_account", "Удалить аккаунт без истории");
        }

        if (IsTable)
        {
            var index = Actions.ToList().FindIndex(a => a.Key == "export");
            if (index >= 0)
                Actions[index] = Actions[index] with { Label = Actions[index].Label + " в Word" };
            else
                Add("export", "Выгрузить запись в Word");
            Add("export_excel", "Выгрузить запись в Excel");
        }
        if (Gateway is HttpClinicGateway permitted)
            foreach (var action in Actions.Where(a => !AccessPresentation.Action(role, SelectedSection?.EditKind, a.Key, permitted.Permissions)).ToArray())
                Actions.Remove(action);
    }

    [RelayCommand]
    private async Task RowActionAsync(string key)
    {
        var row = SelectedRow;
        if (row is null || IsBusy || IsActionConfirmOpen)
            return;
        switch (key)
        {
            case "edit_permissions":
                await OpenEditorAsync("role_permissions", int.Parse(row.Values["role_id"]), new());
                return;
            case "edit":
                await OpenEditorAsync(SelectedSection!.EditKind!, row.Id, new(row.Values));
                return;
            case "book_slot":
                await OpenEditorAsync("booking", 0, new(row.Values));
                return;
            case "reschedule":
                await OpenEditorAsync("booking", row.Id, new(row.Values));
                return;
            case "result":
                await OpenEditorAsync("result", row.Id, new(row.Values));
                return;
            case "payment":
                var total = decimal.Parse(row.Values["total_text"], CultureInfo.InvariantCulture);
                var paid = decimal.Parse(row.Values["paid"], CultureInfo.InvariantCulture);
                await OpenEditorAsync("payment", row.Id, new() { ["amount"] = (total - paid).ToString(CultureInfo.InvariantCulture) });
                return;
            case "new_prescription":
                await OpenEditorAsync("prescription", 0, new() { ["visit_id"] = row.Id.ToString() });
                return;
            case "new_referral":
                await OpenEditorAsync("referral", 0, new() { ["visit_id"] = row.Id.ToString() });
                return;
            case "new_document":
                await OpenEditorAsync("document", 0, new() { ["patient_id"] = row.Id.ToString() });
                return;
            case "history":
                var patient = row.Id;
                SelectedSection = Sections.First(s => s.Key == "visits");
                await RefreshAsync();
                PatientFilter = patient;
                Filter();
                return;
            case "export":
            case "export_excel":
            case "download":
                await FileAsync(key);
                return;
        }

        var confirmation = key switch
        {
            "delete_referral" or "delete_schedule" or "delete_prescription" or "delete_rendered" or "delete_dictionary" or "delete_account" => ("Удалить запись?", "Запись будет удалена. Связанные медицинские данные защищены: сервер отклонит удаление, если запись используется.", "Удалить"),
            "cancel_invoice" => ("Отменить счёт?", "Неоплаченные услуги освободятся для нового счёта.", "Отменить счёт"),
            "create_invoice" => ("Выставить счёт?", "В счёт войдёт только выбранная услуга с указанным количеством и ценой.", "Выставить счёт"),
            "cancel" => ("Отменить запись?", "Выбранное время станет доступно для записи другим пациентам.", "Отменить запись"),
            "cancel_referral" => ("Отменить направление?", "По этому направлению больше нельзя будет внести результат исследования.", "Отменить направление"),
            "no_show" => ("Отметить неявку?", "Убедитесь, что пациент не пришёл на приём.", "Отметить неявку"),
            "close_visit" => ("Завершить приём?", "После завершения протокол приёма нельзя будет редактировать. Проверьте диагноз и рекомендации.", "Завершить приём"),
            "toggle_patient" when row.Status != "архив" => ("Архивировать пациента?", "Карта и история посещений сохранятся. Пациента можно восстановить из архива.", "Архивировать"),
            "toggle_account" when row.Status == "активен" => ("Заблокировать пользователя?", "Пользователь не сможет входить в систему до разблокировки учётной записи.", "Заблокировать"),
            "toggle_employee" or "toggle_service" when row.Status != "архив" => ("Архивировать запись?", "Запись сохранится в истории и станет недоступна при выборе в новых формах.", "Архивировать"),
            _ => ("", "", "")};
        if (confirmation.Item1.Length > 0 && !await ConfirmActionAsync(confirmation.Item1, confirmation.Item2 + "\n\n" + row.Cell1, confirmation.Item3))
            return;
        IsBusy = true;
        try
        {
            await Gateway.ActAsync(Session, key, row.Id, new(row.Values));
            if (key == "start_visit")
            {
                SelectedSection = Sections.First(s => s.Key == "visits");
                await RefreshAsync();
                SelectedRow = Rows.FirstOrDefault(r => r.Values.GetValueOrDefault("appointment_id") == row.Id.ToString());
                if (SelectedRow is not null)
                {
                    IsBusy = false;
                    await OpenEditorAsync("visit", SelectedRow.Id, new(SelectedRow.Values));
                }
            }
            else
                await RefreshAsync();
            ShowSuccess("Изменения сохранены.");
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
}
