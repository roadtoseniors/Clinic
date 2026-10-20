using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Clinic.Data;
using Clinic.Workspace;
using CommunityToolkit.Mvvm.Input;

namespace Clinic.ViewModels;

public partial class WorkspaceViewModel
{
    public Task<AttachmentInfo?> PickAttachmentAsync(IEnumerable<AttachmentInfo> files)
    {
        if (_attachmentChoice is not null)
        {
            return Task.FromResult<AttachmentInfo?>(null);
        }

        Replace(AttachmentChoices, files);
        IsAttachmentPickerOpen = true;
        _attachmentChoice = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return _attachmentChoice.Task;
    }

    [RelayCommand]
    private void ChooseAttachment(AttachmentInfo file)
    {
        FinishAttachmentChoice(file);
    }

    [RelayCommand]
    private void CancelAttachmentChoice()
    {
        FinishAttachmentChoice(null);
    }

    private void FinishAttachmentChoice(AttachmentInfo? file)
    {
        IsAttachmentPickerOpen = false;
        var pendingChoice = _attachmentChoice;
        _attachmentChoice = null;
        pendingChoice?.TrySetResult(file);
    }

    [RelayCommand]
    private async Task DeleteAttachmentAsync(AttachmentInfo file)
    {
        if (Gateway is not HttpClinicGateway api || !CanDeleteAttachment || IsBusy)
        {
            return;
        }

        var confirmed = await ConfirmActionAsync("Удалить файл?",
            file.Name + "\nФайл будет удалён из результата исследования.", "Удалить");
        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await api.DeleteAttachmentAsync(Session, file.Id);
            AttachmentChoices.Remove(file);
            if (AttachmentChoices.Count == 0)
            {
                FinishAttachmentChoice(null);
            }

            await RefreshAsync();
            ShowSuccess("Файл удалён.");
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

    public Task<bool> ConfirmDiscardAsync()
    {
        return ConfirmActionAsync("Отменить изменения?", "Несохранённые изменения будут потеряны.", "Отменить изменения");
    }

    private Task<bool> ConfirmActionAsync(string title, string description, string label)
    {
        if (_actionConfirmation is not null)
        {
            // Не открываем второй диалог поверх ожидающего подтверждения.
            return Task.FromResult(false);
        }

        ConfirmTitle = title;
        ConfirmDescription = description;
        ConfirmLabel = label;
        _actionConfirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IsActionConfirmOpen = true;
        return _actionConfirmation.Task;
    }

    [RelayCommand]
    private void CancelAction()
    {
        FinishActionConfirmation(false);
    }

    [RelayCommand]
    private void ConfirmAction()
    {
        FinishActionConfirmation(true);
    }

    private void FinishActionConfirmation(bool confirmed)
    {
        IsActionConfirmOpen = false;
        var pendingConfirmation = _actionConfirmation;
        _actionConfirmation = null;
        pendingConfirmation?.TrySetResult(confirmed);
    }

    public Task<bool> ConfirmRestoreAsync()
    {
        if (_restoreConfirmation is not null)
        {
            return Task.FromResult(false);
        }

        RestoreConfirmText = "";
        IsRestoreConfirmOpen = true;
        _restoreConfirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return _restoreConfirmation.Task;
    }

    [RelayCommand]
    private void CancelRestore()
    {
        FinishRestoreConfirmation(false);
    }

    [RelayCommand]
    private void ConfirmRestore()
    {
        if (CanConfirmRestore)
        {
            FinishRestoreConfirmation(true);
        }
    }

    private void FinishRestoreConfirmation(bool confirmed)
    {
        IsRestoreConfirmOpen = false;
        var pendingConfirmation = _restoreConfirmation;
        _restoreConfirmation = null;
        pendingConfirmation?.TrySetResult(confirmed);
    }
}
