using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Clinic.ViewModels;

namespace Clinic.Views;
public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
        KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Escape || DataContext is not WorkspaceViewModel model)
                return;
            if (model.IsActionConfirmOpen)
                model.CancelActionCommand.Execute(null);
            else if (model.IsAttachmentPickerOpen)
                model.CancelAttachmentChoiceCommand.Execute(null);
            else if (model.IsRestoreConfirmOpen)
                model.CancelRestoreCommand.Execute(null);
            else if (model.IsEditorOpen)
                await model.CloseEditorCommand.ExecuteAsync(null);
            else
                model.CloseDetailsCommand.Execute(null);
            e.Handled = true;
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is WorkspaceViewModel model)
                model.FileAction = kind => HandleFileAsync(model, kind);
        };
    }

    private async Task HandleFileAsync(WorkspaceViewModel model, string kind)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;
        try
        {
            if (kind == "attach")
            {
                var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = "Файл результата", AllowMultiple = true, FileTypeFilter = [new("Результат исследования") { Patterns = ["*.pdf", "*.jpg", "*.jpeg", "*.png"] }] });
                if (files.Count == 0)
                    return;
                if (files.Count > 10)
                    throw new InvalidOperationException("Выберите не более 10 файлов.");
                foreach (var selectedFile in files)
                {
                    await using var stream = await selectedFile.OpenReadAsync();
                    using var memory = new MemoryStream();
                    var buffer = new byte[8192];
                    int count;
                    while ((count = await stream.ReadAsync(buffer)) > 0)
                    {
                        if (memory.Length + count > 10 * 1024 * 1024)
                            throw new InvalidOperationException("Максимальный размер файла — 10 МБ.");
                        await memory.WriteAsync(buffer.AsMemory(0, count));
                    }

                    if (!model.Attach(selectedFile.Name, memory.ToArray()))
                        return;
                }

                return;
            }

            if (kind == "restore")
            {
                var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = "Выберите резервную копию", AllowMultiple = false, FileTypeFilter = [new("Копия поликлиники") { Patterns = ["*.json"] }] });
                if (files.Count == 0)
                    return;
                await using var read = await files[0].OpenReadAsync();
                using var reader = new StreamReader(read);
                var json = await reader.ReadToEndAsync();
                if (!await model.ConfirmRestoreAsync())
                    return;
                model.IsBusy = true;
                await model.Gateway.RestoreAsync(model.Session, json);
                await model.RefreshCommand.ExecuteAsync(null);
                model.ShowSuccess("Данные восстановлены из копии.");
                return;
            }

            byte[] bytes;
            string filename;
            if (kind == "download")
            {
                if (model.SelectedRow is null)
                    return;
                if (model.Gateway is Clinic.Data.HttpClinicGateway api)
                {
                    var files = await api.AttachmentsAsync(model.Session, model.SelectedRow.Id);
                    if (files.Count == 0)
                        throw new InvalidOperationException("Файл не найден.");
                    var selected = files.Count == 1 && !model.CanDeleteAttachment ? files[0] : await model.PickAttachmentAsync(files);
                    if (selected is null)
                        return;
                    model.IsBusy = true;
                    bytes = await api.DownloadAttachmentAsync(model.Session, selected.Id);
                    filename = Path.GetFileName(selected.Name);
                }
                else
                {
                    bytes = await model.Gateway.AttachmentAsync(model.Session, model.SelectedRow.Id) ?? throw new InvalidOperationException("Файл не найден.");
                    filename = Path.GetFileName(model.SelectedRow.Values.GetValueOrDefault("file_name", "result.bin"));
                }
            }
            else if (kind == "backup")
            {
                model.IsBusy = true;
                bytes = Encoding.UTF8.GetBytes(await model.Gateway.BackupAsync(model.Session));
                filename = "clinic-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json";
            }
            else
            {
                bytes = Encoding.UTF8.GetBytes(model.ExportHtml(kind == "export_all"));
                filename = "clinic-" + (model.SelectedSection?.Key ?? "document") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".html";
            }

            var file = await top.StorageProvider.SaveFilePickerAsync(new() { Title = "Сохранить файл", SuggestedFileName = filename });
            if (file is null)
                return;
            await using var write = await file.OpenWriteAsync();
            write.SetLength(0);
            await write.WriteAsync(bytes);
            model.ShowSuccess("Файл сохранён: " + file.Name);
        }
        catch (Exception ex)
        {
            if (kind == "attach")
                model.EditorError = WorkspaceViewModel.Friendly(ex);
            else
                model.ShowError(WorkspaceViewModel.Friendly(ex));
        }
        finally
        {
            model.IsBusy = false;
        }
    }
}
