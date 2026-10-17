using GestionLibros.Data.Import;
using GestionLibros.Models;
using GestionLibros.Reporting;

// Partida: Excel export, template import and the "Reporte de Asientos Contables" print layout.
static class TransferChecks
{
    static int passed;
    static void Check(bool condition, string description) { if (!condition) throw new Exception(description); passed++; Console.WriteLine("PASS TRANSFER " + description); }
    static void Rejected(IReadOnlyList<string[]> rows, string expected, string description)
    {
        try { JournalTemplate.Parse(rows); }
        catch (FormatException ex) when (ex.Message.Contains(expected)) { passed++; Console.WriteLine("PASS TRANSFER " + description); return; }
        throw new Exception("Expected template error: " + description);
    }

    static string[] Row(params string[] cells) => cells;

    public static void Run()
    {
        var header = JournalTemplate.Headers;
        var valid = new List<string[]>
        {
            header,
            Row("02", "6104910", "6/10/2026", "Reg. Traslado de fondos", "1.21102", "", "1083440.91", ""),
            Row("", "", "", "", "1.12101", "Traslado BANPAIS", "", "1,083,440.91"),
            Row("", "", "", "", "", "", "", ""),
        };
        var journal = JournalTemplate.Parse(valid);
        Check(journal.Type == "02" && journal.Reference == "6104910" && journal.Date == new DateTime(2026, 10, 6) && journal.Concept == "Reg. Traslado de fondos",
            "Template header comes from the first row");
        Check(journal.Lines.Count == 2 && journal.Debits == 108344091 && journal.Credits == 108344091 && journal.Lines[1].Concept == "Traslado BANPAIS",
            "Template movements keep account, concept, cargo and crédito in cents");

        // Round trip through a real .xlsx, with an Excel date serial and float residue as Excel may store them.
        using (var file = new MemoryStream())
        {
            Xlsx.Write(file, new Xlsx.Sheet("Partida", [
                header,
                new object?[] { "02", "6104910", "46301", "Reg. Traslado de fondos", "1.21102", null, 1083440.9099999999m, null },
                new object?[] { null, null, null, null, "1.12101", null, null, 1083440.91m }]));
            file.Position = 0;
            var fromExcel = JournalTemplate.Parse(Xlsx.Read(file));
            Check(fromExcel.Date == new DateTime(2026, 10, 6) && fromExcel.Debits == 108344091 && fromExcel.Credits == 108344091,
                "Template read from .xlsx accepts Excel date serials and float residue");
        }

        Rejected([Row("Tipo", "Ref", "Fecha", "Concepto de la póliza", "Cuenta", "Concepto del movimiento", "Cargo", "Crédito")], "columna B", "Renamed columns are rejected");
        Rejected([header, Row("01", "1", "1/1/2026", "X", "1.1", "", "10", "10"), Row("", "", "", "", "2.1", "", "", "10")], "uno solo", "Cargo and crédito on the same row are rejected");
        Rejected([header, Row("01", "1", "1/1/2026", "X", "1.1", "", "10", ""), Row("", "", "", "", "2.1", "", "", "9.99")], "no son iguales", "Unbalanced partida is rejected");
        Rejected([header, Row("01", "1", "1/1/2026", "X", "1.1", "", "10", ""), Row("01", "2", "", "", "2.1", "", "", "10")], "Renglón 3: Referencia", "A second póliza in the same file is rejected");
        Rejected([header, Row("01", "1", "31/02/2026", "X", "1.1", "", "10", ""), Row("", "", "", "", "2.1", "", "", "10")], "fecha", "Invalid date is rejected");
        Rejected([header, Row("01", "1", "1/1/2026", "X", "1.1", "", "10.005", ""), Row("", "", "", "", "2.1", "", "", "10.005")], "Cargo", "More than two decimals are rejected");
        Rejected([header, Row("01", "12345678", "1/1/2026", "X", "1.1", "", "10", ""), Row("", "", "", "", "2.1", "", "", "10")], "más de 7", "Reference longer than 7 characters is rejected");
        Rejected([header, Row("", "1", "1/1/2026", "X", "1.1", "", "10", ""), Row("", "", "", "", "2.1", "", "", "10")], "Tipo", "Missing Tipo is rejected");
        Rejected([header, Row("01", "1", "1/1/2026", "X", "1.1", "", "-10", ""), Row("", "", "", "", "2.1", "", "", "10")], "Cargo", "Negative amount is rejected");

        var csv = JournalTemplate.ReadCsv("Tipo;Referencia;Fecha;Concepto de la poliza;Cuenta;Concepto del movimiento;Cargo;Credito\r\n01;7;2/1/2026;\"Pago; renta\";5.1;;250;\r\n;;;;1.1;;;250\r\n");
        var fromCsv = JournalTemplate.Parse(csv);
        Check(fromCsv.Concept == "Pago; renta" && fromCsv.Lines.Count == 2, "Semicolon CSV with titles without accents is accepted");

        var print = new PolicyPrint(new Journal { Type = "02", Reference = "6104910", Date = new DateTime(2026, 10, 6), Concept = "Reg. Traslado de fondos" }, "CHEQUES",
            [new("1.21102", "Estación Entrada Maya", "Reg. Traslado de fondos", 108344091, 0), new("1.12101", "BANPAIS #01-080-000176-9", "Reg. Traslado de fondos", 0, 108344091)]);
        var document = ReportFormatter.JournalEntries("INVAYAL (ESTACIÓN SANTA BÁRBARA)", [print]);
        Check(document.Title == "Reporte de Asientos Contables" && document.Headers.SequenceEqual(new[] { "Pol  Ref", "Fecha", "Cuenta", "Descripción", "Concepto", "Cargos", "Créditos" }),
            "Printed partida uses the GL Reporte de Asientos Contables columns");
        Check(document.Rows[0].Cells[0].Value == "02  6104910" && document.Rows[1].Cells[0].Value == "" && document.Rows[^1].Summary && document.Rows[^1].Cells[4].Value == "Totales:",
            "Póliza and date print once, followed by Totales");
        var html = ReportFormatter.Html(document, false, false);
        if (Environment.GetEnvironmentVariable("FREDI_REPORT_PREVIEW_DIRECTORY") is { Length: > 0 } preview)
        {
            Directory.CreateDirectory(preview);
            File.WriteAllText(Path.Combine(preview, "asientos-gl.html"), html);
        }
        Check(!html.Contains("class='period'") && html.Contains("Reporte de Asientos Contables"), "Asientos report prints company and title without a period line");

        using (var file = new MemoryStream())
        {
            Xlsx.Write(file, ReportFormatter.JournalEntriesSheet("INVAYAL", [print]));
            file.Position = 0;
            var sheet = Xlsx.Read(file);
            Check(sheet[3][0] == "Pol  Ref" && sheet[4][2] == "1.21102" && sheet[4][5] == "1083440.91" && sheet[6][4] == "Totales:" && sheet[6][6] == "1083440.91",
                "Excel export has the same columns with numeric amounts and totals");
        }
        // Account codes Excel stored as numbers.
        string[] catalog = ["1.12100", "1.12104", "1.14203", "5.30005", "01"];
        Check(JournalTemplate.Resolve("1.121", catalog, out _) == "1.12100" && JournalTemplate.Resolve("1", catalog, out _) == "01",
            "Codes Excel turned into numbers match the catalog by value (1.121 → 1.12100, 1 → 01)");
        Check(JournalTemplate.Resolve("9.9", catalog, out var none) == null && !none, "Unknown code stays unresolved");
        Check(JournalTemplate.Resolve("1.1", ["1.1", "1.10"], out _) == "1.1" && JournalTemplate.Resolve("1.10000", ["1.1", "1.10"], out var twice) == null && twice,
            "Exact text wins; two codes with the same value are reported as ambiguous");
        using (var file = new MemoryStream())
        {
            Xlsx.Write(file, new Xlsx.Sheet("Partida", [JournalTemplate.Headers,
                new object?[] { "02", "6090003", "46325", "X", 5.3000499999999997m, null, 10m, null }]));
            file.Position = 0;
            var read = Xlsx.Read(file);
            Check(read[1][4] == "5.30005", "Numeric cells are read in their shortest exact form (5.3000499999999997 → 5.30005)");
        }
        using (var file = new MemoryStream())
        {
            Xlsx.Write(file, new Xlsx.Sheet("Partida", [JournalTemplate.Headers], TextColumns: [0, 1, 4]));
            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(file.ToArray()));
            var sheetXml = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ReadToEnd();
            Check(sheetXml.Contains("<col min=\"5\" max=\"5\" width=\"12\" customWidth=\"1\" style=\"4\"/>"), "Blank template formats the Cuenta column as Text");
        }

        // The user's real template (read only), when present on this machine.
        const string userFile = @"C:\Users\Santa Barbara\Downloads\Plantilla-Partida-20261017-105544.xlsx";
        if (File.Exists(userFile))
        {
            using var stream = new FileStream(userFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var real = JournalTemplate.Parse(Xlsx.Read(stream));
            Check(real.Lines.Count == 54 && real.Lines.Any(l => l.AccountCode == "5.30005") && real.Lines.Any(l => l.AccountCode == "1.14203"),
                "User template reads 54 movements with exact account codes");
            const string glCatalog = @"C:\Sistemas\Datos\003\Cat00326.Tps";
            if (File.Exists(glCatalog))
            {
                var codes = GlCatalog.Read(glCatalog).Select(a => a.Code).ToList();
                var missing = real.Lines.Where(l => JournalTemplate.Resolve(l.AccountCode, codes, out _) == null).Select(l => l.AccountCode).Distinct().ToList();
                Console.WriteLine($"      cuentas sin coincidencia en el catálogo GL 003: {(missing.Count == 0 ? "ninguna" : string.Join(", ", missing))}");
                Check(missing.Count == 0, "Every account of the user template exists in the GL 003 catalog");
            }
        }
        Console.WriteLine($"All {passed} transfer checks passed.");
    }
}
