using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Clinic.Export;

public static class WordExporter
{
    public static byte[] Create(ExportTable data)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new Styles(
                new Style(new StyleName { Val = "Normal" },
                    new StyleParagraphProperties(new SpacingBetweenLines { After = "120" }),
                    new StyleRunProperties(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" },
                        new Color { Val = "000000" }, new FontSize { Val = "22" }))
                { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
                new Style(new StyleName { Val = "Title" }, new BasedOn { Val = "Normal" },
                    new StyleParagraphProperties(new KeepNext(), new SpacingBetweenLines { After = "200" }),
                    new StyleRunProperties(new Bold(), new Color { Val = "000000" }, new FontSize { Val = "36" }))
                { Type = StyleValues.Paragraph, StyleId = "Title" });

            var body = new Body();
            main.Document = new Document(body);
            body.Append(Paragraph("Медицинская информационная система"));
            var title = Paragraph(data.Title);
            title.ParagraphProperties = new ParagraphProperties(new ParagraphStyleId { Val = "Title" });
            body.Append(title, Paragraph(data.Context),
                Paragraph($"Сформировано {data.CreatedAt:dd.MM.yyyy HH:mm} · {data.Author}"));

            var landscape = data.Columns.Count >= 5;
            var pageWidth = landscape ? 15840u : 12240u;
            var pageHeight = landscape ? 12240u : 15840u;
            var contentWidth = (int)pageWidth - 2016;
            var weights = data.Columns.Select((_, i) => ExportValues.ColumnWidth(data, i)).ToArray();
            var widths = weights.Select(w => (int)(contentWidth * w / weights.Sum())).ToArray();
            widths[^1] += contentWidth - widths.Sum();

            var table = new Table(new TableProperties(
                new TableWidth { Width = contentWidth.ToString(), Type = TableWidthUnitValues.Dxa },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Color = "D7E1E7", Size = 4 },
                    new LeftBorder { Val = BorderValues.Single, Color = "D7E1E7", Size = 4 },
                    new BottomBorder { Val = BorderValues.Single, Color = "D7E1E7", Size = 4 },
                    new RightBorder { Val = BorderValues.Single, Color = "D7E1E7", Size = 4 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Color = "D7E1E7", Size = 4 },
                    new InsideVerticalBorder { Val = BorderValues.Single, Color = "D7E1E7", Size = 4 }),
                new TableLayout { Type = TableLayoutValues.Fixed },
                new TableCellMarginDefault(
                    new TopMargin { Width = "100", Type = TableWidthUnitValues.Dxa },
                    new LeftMargin { Width = "100", Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "100", Type = TableWidthUnitValues.Dxa },
                    new RightMargin { Width = "100", Type = TableWidthUnitValues.Dxa })),
                new TableGrid(widths.Select(w => new GridColumn { Width = w.ToString() })));
            var header = new TableRow(new TableRowProperties(new TableHeader()));
            for (var column = 0; column < data.Columns.Count; column++)
                header.Append(Cell(data.Columns[column].Title, widths[column], true));
            table.Append(header);
            foreach (var values in data.Rows)
            {
                var row = new TableRow();
                for (var column = 0; column < data.Columns.Count; column++)
                    row.Append(Cell(values[column], widths[column], false));
                table.Append(row);
            }
            body.Append(table);
            if (data.Rows.Count == 0) body.Append(Paragraph("Нет записей по выбранным условиям"));

            if (data.Details.Length > 0)
            {
                var heading = Paragraph("Подробности", true);
                heading.ParagraphProperties = new ParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = "240", After = "140" });
                body.Append(heading);
                foreach (var line in data.Details.Replace("\r", "").Split('\n')) body.Append(Paragraph(line));
            }

            var footer = main.AddNewPart<FooterPart>();
            footer.Footer = new Footer(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Right }),
                new SimpleField(new Run(new Text("1"))) { Instruction = " PAGE " }));
            body.Append(new SectionProperties(
                new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) },
                new PageSize { Width = pageWidth, Height = pageHeight, Orient = landscape ? PageOrientationValues.Landscape : PageOrientationValues.Portrait },
                new PageMargin { Top = 1008, Bottom = 1008, Left = 1008, Right = 1008, Header = 540, Footer = 540 }));
            document.PackageProperties.Title = data.Title;
            document.PackageProperties.Creator = data.Author;
            document.PackageProperties.Created = data.CreatedAt;
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static TableCell Cell(string text, int width, bool header)
    {
        var properties = new TableCellProperties(new TableCellWidth { Width = width.ToString(), Type = TableWidthUnitValues.Dxa });
        if (header) properties.Append(new Shading { Fill = "E8F1F5", Val = ShadingPatternValues.Clear });
        properties.Append(new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top });
        var paragraph = Paragraph(text, header);
        paragraph.ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines { After = "0" });
        return new TableCell(properties, paragraph);
    }

    private static Paragraph Paragraph(string text, bool bold = false)
    {
        var paragraph = new Paragraph();
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            if (paragraph.ChildElements.Count > 0) paragraph.Append(new Run(new Break()));
            var run = new Run();
            if (bold) run.RunProperties = new RunProperties(new Bold());
            run.Append(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
            paragraph.Append(run);
        }
        return paragraph;
    }
}
