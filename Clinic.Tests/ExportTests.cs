using System.IO.Compression;
using System.Xml.Linq;
using Clinic.Demo;
using Clinic.Export;
using Clinic.ViewModels;
using Clinic.Workspace;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;

internal static class ExportTests
{
    public static async Task<int> RunAsync(string? sampleDirectory = null)
    {
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            checks++;
        }
        void ValidWord(byte[] bytes)
        {
            using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
            var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).Take(5).ToArray();
            Check(errors.Length == 0, "Valid DOCX: " + string.Join("; ", errors.Select(e => e.Description)));
        }
        void ValidExcel(byte[] bytes)
        {
            using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
            var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).Take(5).ToArray();
            Check(errors.Length == 0, "Valid XLSX: " + string.Join("; ", errors.Select(e => e.Description)));
        }

        var date = new DateTime(2026, 10, 20, 10, 30, 0);
        var patients = new ExportTable("Пациенты", "Активные медицинские карты · Записей: 2", "Волкова Екатерина Александровна", date,
            [new("ФИО"), new("Дата рождения", ExportCellKind.Date), new("Полис ОМС"), new("Телефон"), new("Статус")],
            [
                ["Лебедева Ольга Сергеевна", "01.07.1992", "0000123456789012", "+7 (900) 200-30-01", "активен"],
                ["=HYPERLINK(\"example\") & <Текст>", "14.03.1985", "0000000000000001", "+7 (900) 200-30-02", "активен"]
            ]);
        var excel = ExcelExporter.Create(patients);
        ValidExcel(excel);
        using (var workbook = new XLWorkbook(new MemoryStream(excel)))
        {
            var sheet = workbook.Worksheet(1);
            Check(sheet.Cell("A1").GetString() == "Пациенты", "Excel title");
            Check(sheet.Cell("C6").GetString() == "0000123456789012" && sheet.Cell("C6").DataType == XLDataType.Text, "Policy keeps leading zeroes");
            Check(sheet.Cell("D6").GetString() == "+7 (900) 200-30-01", "Phone remains text");
            Check(sheet.Cell("B6").GetDateTime() == new DateTime(1992, 7, 1), "Date is a sortable date value");
            Check(!sheet.Cell("A7").HasFormula && sheet.Cell("A7").GetString() == patients.Rows[1][0], "Formula-like input remains literal text");
            Check(sheet.AutoFilter.IsEnabled, "Excel filters are enabled");
            Check(sheet.Row(5).Height >= 30 && sheet.Column(3).Width >= 19, "Readable Excel header and identifier column");
        }
        using (var archive = new ZipArchive(new MemoryStream(excel)))
        {
            using var source = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
            var xml = XDocument.Load(source);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Check(xml.Descendants(ns + "pane").Single().Attribute("ySplit")!.Value == "5", "Header remains frozen while scrolling");
        }

        var word = WordExporter.Create(patients);
        ValidWord(word);
        using (var doc = WordprocessingDocument.Open(new MemoryStream(word), false))
        {
            Check(doc.MainDocumentPart!.Document!.InnerText.Contains(patients.Rows[1][0]), "Word preserves special characters");
            Check(doc.MainDocumentPart!.Document!.Descendants<TableHeader>().Any(), "Word table repeats its header");
            Check(doc.MainDocumentPart!.Document!.Descendants<PageSize>().Single().Orient?.Value == PageOrientationValues.Landscape, "Wide Word list uses landscape pages");
            Check(doc.MainDocumentPart.StyleDefinitionsPart!.Styles!.Elements<Style>().Any(s => s.StyleId == "Title"), "Word has an editable Title style");
        }

        var report = new ExportTable("Отчёты", "20.10.2026", "Администратор", date,
            [new("Показатель"), new("Значение", ExportCellKind.Metric)],
            [["Загрузка", "76,5 %"], ["Выручка", "1 250,50 ₽"], ["Посещения", "12"]]);
        using (var workbook = new XLWorkbook(new MemoryStream(ExcelExporter.Create(report))))
        {
            var sheet = workbook.Worksheet(1);
            Check(Math.Abs(sheet.Cell("B6").GetDouble() - 0.765) < 0.00001, "Percentage is a numeric fraction");
            Check(sheet.Cell("B7").GetDouble() == 1250.5, "Currency is numeric, without losing cents");
            Check(sheet.Cell("B8").GetDouble() == 12, "Report count is numeric");
        }

        var empty = patients with { Rows = [] };
        ValidWord(WordExporter.Create(empty));
        var emptyExcel = ExcelExporter.Create(empty);
        ValidExcel(emptyExcel);
        using (var workbook = new XLWorkbook(new MemoryStream(emptyExcel)))
            Check(workbook.Worksheet(1).Cell("A6").GetString().StartsWith("Нет записей"), "Empty list has an explanation");
        var details = "Пациент: Лебедева Ольга Сергеевна\nЖалобы: контрольный осмотр\nРекомендации: повторный приём по назначению врача.\n" + new string('Я', 900);
        var record = patients with { Title = "Карточка пациента", Rows = [patients.Rows[0]], Details = details };
        var recordWord = WordExporter.Create(record);
        ValidWord(recordWord);
        using (var doc = WordprocessingDocument.Open(new MemoryStream(recordWord), false))
            Check(doc.MainDocumentPart!.Document!.InnerText.Contains(details.Split('\n')[^1]), "Long record notes are not lost in Word");
        var recordExcel = ExcelExporter.Create(record);
        ValidExcel(recordExcel);
        using (var workbook = new XLWorkbook(new MemoryStream(recordExcel)))
        {
            var cells = workbook.Worksheet(1).CellsUsed().Select(c => c.GetString()).ToArray();
            Check(cells.Any(c => c == "Подробности") && string.Concat(cells).Contains(new string('Я', 900)), "Long notes split into readable Excel rows without truncation");
        }

        var model = new MainViewModel(() => new DemoGateway()) { Login = "petrova", Password = "demo123" };
        await model.SignInCommand.ExecuteAsync(null);
        var workspace = model.Workspace!;
        workspace.SelectedSection = workspace.Sections.First(s => s.Key == "patients");
        await workspace.RefreshCommand.ExecuteAsync(null);
        workspace.Search = workspace.Rows[0].Cell1;
        Check(workspace.Rows.Count == 1, "Export test has an active search filter");
        var filtered = workspace.ExportData(true);
        Check(filtered.Rows.Count == 1 && filtered.Context.Contains(workspace.Search), "Export respects search filters and describes them");
        workspace.SelectedRow = workspace.Rows.Single();
        Check(workspace.Actions.Any(a => a.Key == "export") && workspace.Actions.Any(a => a.Key == "export_excel"), "Patient card has both export formats");
        var selected = workspace.ExportData(false);
        Check(selected.Rows.Count == 1 && selected.Details == workspace.SelectedRow.Details, "Selected export includes full record details");
        var excelFile = workspace.CreateExport("export_all_excel");
        var wordFile = workspace.CreateExport("export");
        Check(excelFile.Name.EndsWith(".xlsx") && excelFile.ContentType.Contains("spreadsheetml"), "Excel command has correct name and MIME type");
        Check(wordFile.Name.EndsWith(".docx") && wordFile.ContentType.Contains("wordprocessingml"), "Word command has correct name and MIME type");
        ValidExcel(excelFile.Content);
        ValidWord(wordFile.Content);
        workspace.SelectedRow = null;
        try { workspace.CreateExport("export"); throw new Exception("Missing selection should be rejected"); }
        catch (InvalidOperationException) { checks++; }
        workspace.Search = "Not found";
        Check(workspace.ExportData(true).Rows.Count == 0, "Empty filtered export does not leak unfiltered rows");
        ValidExcel(workspace.CreateExport("export_all_excel").Content);
        ValidWord(workspace.CreateExport("export_all_word").Content);
        workspace.SelectedSection = workspace.Sections.First(s => s.Key == "appointments");
        await workspace.RefreshCommand.ExecuteAsync(null);
        workspace.SelectedRow = workspace.Rows.First();
        Check(workspace.Actions.Any(a => a.Key == "export_excel"), "Appointment has a visible Excel action");
        string? invoked = null;
        workspace.FileAction = action => { invoked = action; ValidExcel(workspace.CreateExport(action).Content); return Task.CompletedTask; };
        await workspace.RowActionCommand.ExecuteAsync("export_excel");
        Check(invoked == "export_excel", "Row command reaches the Excel file handler");
        invoked = null;
        await workspace.FileCommand.ExecuteAsync("export_all_excel");
        Check(invoked == "export_all_excel", "List command reaches the Excel file handler");
        await TestUi.SignOutAsync(model);

        if (sampleDirectory is not null)
        {
            Directory.CreateDirectory(sampleDirectory);
            await File.WriteAllBytesAsync(Path.Combine(sampleDirectory, "patients.docx"), word);
            await File.WriteAllBytesAsync(Path.Combine(sampleDirectory, "patients.xlsx"), excel);
            await File.WriteAllBytesAsync(Path.Combine(sampleDirectory, "patient-card.docx"), recordWord);
        }
        return checks;
    }
}
