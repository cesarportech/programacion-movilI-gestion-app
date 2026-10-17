using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace GestionLibros.Reporting;

// Minimal Excel (.xlsx) writer and reader, without external packages. An .xlsx is a ZIP of XML parts.
// Writer: inline strings, numbers with the "#,##0.00" format, bold header row. Reader: first sheet,
// shared and inline strings, numbers as invariant text.
public static class Xlsx
{
    // HeaderRow: index of the row drawn as the grey column bar (-1 = none).
    // TextColumns: zero-based columns formatted as Text ("@"), so Excel keeps what is typed (account codes
    // like 1.12100 would otherwise become the number 1.121).
    public sealed record Sheet(string Name, IReadOnlyList<IReadOnlyList<object?>> Rows, int HeaderRow = 0, double[]? Widths = null, int[]? TextColumns = null);

    public static void Write(Stream output, params Sheet[] sheets)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        void Part(string name, string xml)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }
        var overrides = string.Concat(sheets.Select((_, i) =>
            $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"));
        Part("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" + overrides + "</Types>");
        Part("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Part("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" +
            string.Concat(sheets.Select((s, i) => $"<sheet name=\"{Escape(SheetName(s.Name))}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) + "</sheets></workbook>");
        Part("xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            string.Concat(sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>")) +
            $"<Relationship Id=\"rId{sheets.Length + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
        // Style 0 = normal, 1 = bold header, 2 = amount #,##0.00, 3 = bold amount (totals).
        Part("xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0.00\"/></numFmts>" +
            "<fonts count=\"2\"><font><sz val=\"10\"/><name val=\"Arial\"/></font><font><b/><sz val=\"10\"/><name val=\"Arial\"/></font></fonts>" +
            "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFC0C0C0\"/></patternFill></fill></fills>" +
            "<borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf/></cellStyleXfs>" +
            "<cellXfs count=\"5\"><xf/><xf fontId=\"1\" fillId=\"2\" applyFont=\"1\" applyFill=\"1\"/><xf numFmtId=\"164\" applyNumberFormat=\"1\"/><xf numFmtId=\"164\" fontId=\"1\" applyNumberFormat=\"1\" applyFont=\"1\"/><xf numFmtId=\"49\" applyNumberFormat=\"1\"/></cellXfs></styleSheet>");
        for (var i = 0; i < sheets.Length; i++) Part($"xl/worksheets/sheet{i + 1}.xml", SheetXml(sheets[i]));
    }

    private static string SheetXml(Sheet sheet)
    {
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        var text = sheet.TextColumns ?? [];
        var columns = Math.Max(sheet.Widths?.Length ?? 0, text.Length == 0 ? 0 : text.Max() + 1);
        if (columns > 0)
        {
            xml.Append("<cols>");
            for (var c = 0; c < columns; c++)
            {
                var width = sheet.Widths != null && c < sheet.Widths.Length ? sheet.Widths[c] : 12;
                // style 4 = Text: cells typed later in this column stay text.
                xml.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{width.ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"{(text.Contains(c) ? " style=\"4\"" : "")}/>");
            }
            xml.Append("</cols>");
        }
        xml.Append("<sheetData>");
        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            xml.Append($"<row r=\"{r + 1}\">");
            var header = r == sheet.HeaderRow;
            for (var c = 0; c < sheet.Rows[r].Count; c++)
            {
                var value = sheet.Rows[r][c];
                var cell = $"{Column(c)}{r + 1}";
                switch (value)
                {
                    case null: break;
                    case Total t: xml.Append($"<c r=\"{cell}\" s=\"3\"><v>{t.Amount.ToString(CultureInfo.InvariantCulture)}</v></c>"); break;
                    case decimal d: xml.Append($"<c r=\"{cell}\" s=\"2\"><v>{d.ToString(CultureInfo.InvariantCulture)}</v></c>"); break;
                    case string s when s.Length == 0: break;
                    default:
                        xml.Append($"<c r=\"{cell}\" t=\"inlineStr\"{(header ? " s=\"1\"" : "")}><is><t xml:space=\"preserve\">{Escape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "")}</t></is></c>");
                        break;
                }
            }
            xml.Append("</row>");
        }
        return xml.Append("</sheetData></worksheet>").ToString();
    }

    // A bold amount cell (totals row).
    public sealed record Total(decimal Amount);

    // Reads the first worksheet as rows of text. Numbers come back in invariant format ("1500.5").
    public static List<string[]> Read(Stream input)
    {
        using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is { } sharedEntry)
            using (var stream = sharedEntry.Open())
                shared = XDocument.Load(stream).Root!.Elements(main + "si").Select(si => string.Concat(si.Descendants(main + "t").Select(t => t.Value))).ToList();
        var sheetEntry = zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml"))
            .OrderBy(e => e.FullName.Length).ThenBy(e => e.FullName, StringComparer.Ordinal).FirstOrDefault()
            ?? throw new InvalidDataException("El archivo de Excel no tiene hojas.");
        using var sheetStream = sheetEntry.Open();
        var rows = new List<string[]>();
        foreach (var row in XDocument.Load(sheetStream).Descendants(main + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            var next = 0;
            foreach (var c in row.Elements(main + "c"))
            {
                var index = c.Attribute("r") is { } r ? ColumnIndex(r.Value) : next;
                next = index + 1;
                var type = (string?)c.Attribute("t");
                var raw = c.Element(main + "v")?.Value ?? "";
                cells[index] = type switch
                {
                    "s" => int.TryParse(raw, out var i) && i < shared.Count ? shared[i] : "",
                    "inlineStr" => string.Concat(c.Descendants(main + "t").Select(t => t.Value)),
                    // Numbers: shortest exact form. Excel stores a typed 5.30005 as 5.3000499999999997.
                    _ => Shortest(raw),
                };
            }
            var width = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
            var values = new string[width];
            for (var i = 0; i < width; i++) values[i] = cells.GetValueOrDefault(i, "");
            rows.Add(values);
        }
        return rows;
    }

    private static string Shortest(string raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && Math.Abs(number) < 1e15
            ? number.ToString(CultureInfo.InvariantCulture) : raw;

    private static string Column(int index)
    {
        var name = "";
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var ch in reference.TakeWhile(char.IsLetter)) index = index * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return index - 1;
    }

    private static string SheetName(string name)
    {
        var clean = string.Concat(name.Where(ch => !"[]:*?/\\".Contains(ch)));
        return clean.Length == 0 ? "Hoja1" : clean[..Math.Min(31, clean.Length)];
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? "";
}
