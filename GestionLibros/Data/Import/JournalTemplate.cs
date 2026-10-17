using System.Globalization;

namespace GestionLibros.Data.Import;

public sealed record TemplateLine(int Row, string AccountCode, string Concept, long DebitCents, long CreditCents);

public sealed record TemplateJournal(string Type, string Reference, DateTime Date, string Concept, IReadOnlyList<TemplateLine> Lines)
{
    public long Debits => Lines.Sum(l => l.DebitCents);
    public long Credits => Lines.Sum(l => l.CreditCents);
}

// Plantilla de importación de partidas: one póliza per file, one row per movement.
//   A Tipo | B Referencia | C Fecha | D Concepto de la póliza | E Cuenta | F Concepto del movimiento | G Cargo | H Crédito
// Tipo, Referencia, Fecha and Concepto go on the first data row (later rows may leave them empty, or repeat
// the same values). Each movement has an amount in Cargo or in Crédito, never both. Every error is reported
// with its row number and nothing is loaded until the whole file is valid and cargos equal créditos.
public static class JournalTemplate
{
    public static readonly string[] Headers = ["Tipo", "Referencia", "Fecha", "Concepto de la póliza", "Cuenta", "Concepto del movimiento", "Cargo", "Crédito"];
    public const int MaxReference = 7;

    public static readonly string[] Instructions =
    [
        "PLANTILLA DE IMPORTACIÓN DE PARTIDAS · FREDI",
        "",
        "1. Llene la hoja \"Partida\" a partir del renglón 2. Un archivo = una póliza; un renglón = un movimiento.",
        "2. Tipo (ej. 01), Referencia (número de póliza, hasta 7 caracteres), Fecha (dd/mm/aaaa) y Concepto de la póliza: escríbalos en el primer renglón. En los demás puede dejarlos vacíos o repetirlos iguales.",
        "3. Cuenta: número exacto de una cuenta de detalle del catálogo (ej. 1.12101). No se aceptan cuentas superiores.",
        "4. Concepto del movimiento: opcional; si se deja vacío se usa el concepto de la póliza.",
        "5. Cargo o Crédito: escriba el importe en UNA de las dos columnas, positivo, con hasta 2 decimales. Deje la otra vacía o en 0.",
        "6. La suma de Cargos debe ser igual a la suma de Créditos.",
        "7. No cambie el orden ni los títulos de las columnas. Puede guardar como .xlsx o .csv.",
        "",
        "Al importar, FREDI revisa todo el archivo: si hay un error indica el renglón y no carga nada.",
    ];

    public static TemplateJournal Parse(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0) throw new FormatException("El archivo está vacío.");
        var header = rows[0].Select(h => h.Trim()).ToArray();
        for (var i = 0; i < Headers.Length; i++)
            if (i >= header.Length || Plain(header[i]) != Plain(Headers[i]))
                throw new FormatException($"La columna {(char)('A' + i)} debe llamarse \"{Headers[i]}\". Use la plantilla de FREDI sin cambiar los títulos.");

        var errors = new List<string>();
        string? type = null, reference = null, concept = null; DateTime? date = null;
        var lines = new List<TemplateLine>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string Cell(int i) => i < row.Length ? row[i].Trim() : "";
            if (Enumerable.Range(0, Headers.Length).All(i => Cell(i).Length == 0)) continue;
            var number = r + 1;

            void Header(ref string? current, string value, string name)
            {
                if (value.Length == 0) return;
                if (current == null) current = value;
                else if (!string.Equals(current, value, StringComparison.Ordinal)) errors.Add($"Renglón {number}: {name} \"{value}\" es distinto al del primer renglón (\"{current}\"). Un archivo es una sola póliza.");
            }
            Header(ref type, Cell(0), "Tipo");
            Header(ref reference, Cell(1), "Referencia");
            Header(ref concept, Cell(3), "Concepto");
            if (Cell(2).Length > 0)
            {
                var parsed = ParseDate(Cell(2));
                if (parsed == null) errors.Add($"Renglón {number}: la fecha \"{Cell(2)}\" no es válida; use dd/mm/aaaa.");
                else if (date == null) date = parsed;
                else if (date != parsed) errors.Add($"Renglón {number}: la fecha es distinta a la del primer renglón.");
            }

            var account = Cell(4);
            if (account.Length == 0) { errors.Add($"Renglón {number}: falta la Cuenta."); continue; }
            var debit = ParseAmount(Cell(6), out var debitError);
            var credit = ParseAmount(Cell(7), out var creditError);
            if (debitError) errors.Add($"Renglón {number}: el Cargo \"{Cell(6)}\" no es un importe válido (positivo, hasta 2 decimales).");
            if (creditError) errors.Add($"Renglón {number}: el Crédito \"{Cell(7)}\" no es un importe válido (positivo, hasta 2 decimales).");
            if (debitError || creditError) continue;
            if ((debit == 0) == (credit == 0)) { errors.Add($"Renglón {number}: escriba el importe en Cargo o en Crédito (uno solo)."); continue; }
            lines.Add(new(number, account, Cell(5), debit, credit));
        }

        if (string.IsNullOrEmpty(type)) errors.Insert(0, "Falta el Tipo de póliza en el primer renglón.");
        if (string.IsNullOrEmpty(reference)) errors.Insert(0, "Falta la Referencia (número de póliza) en el primer renglón.");
        else if (reference.Length > MaxReference) errors.Insert(0, $"La Referencia \"{reference}\" tiene más de {MaxReference} caracteres.");
        if (date == null) errors.Insert(0, "Falta la Fecha en el primer renglón.");
        if (string.IsNullOrEmpty(concept)) errors.Insert(0, "Falta el Concepto de la póliza en el primer renglón.");
        if (lines.Count < 2 && errors.Count == 0) errors.Add("La póliza necesita al menos dos movimientos.");
        if (errors.Count == 0 && lines.Sum(l => l.DebitCents) != lines.Sum(l => l.CreditCents))
            errors.Add($"Los cargos ({lines.Sum(l => l.DebitCents) / 100m:N2}) no son iguales a los créditos ({lines.Sum(l => l.CreditCents) / 100m:N2}).");
        if (errors.Count > 0) throw new FormatException(string.Join("\n", errors.Take(12)) + (errors.Count > 12 ? $"\n… y {errors.Count - 12} errores más." : ""));
        return new(type!, reference!, date!.Value, concept!, lines);
    }

    // Column titles compare without accents, case or spaces ("Credito" = "Crédito").
    private static string Plain(string text) => string.Concat(text.Normalize(System.Text.NormalizationForm.FormD)
        .Where(c => char.IsLetter(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToLowerInvariant();

    // Plain CSV (comma or semicolon, quoted fields), as saved by Excel.
    // Finds the catalog code for a code typed in Excel. Exact text first; when Excel stored the code as a
    // number (1.12100 → 1.121, 01 → 1), the catalog code with the same numeric value. Returns null when
    // nothing matches; ambiguous = more than one catalog code has that value (e.g. 1.1 and 1.10).
    public static string? Resolve(string code, IEnumerable<string> catalog, out bool ambiguous)
    {
        ambiguous = false;
        var codes = catalog as ICollection<string> ?? catalog.ToList();
        if (codes.Contains(code)) return code;
        if (!decimal.TryParse(code, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return null;
        var matches = codes.Where(c => decimal.TryParse(c, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v) && v == value).Take(2).ToList();
        ambiguous = matches.Count > 1;
        return matches.Count == 1 ? matches[0] : null;
    }

    public static List<string[]> ReadCsv(string text)
    {
        var rows = new List<string[]>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length == 0) continue;
            var separator = line.Count(c => c == ';') > line.Count(c => c == ',') ? ';' : ',';
            var fields = new List<string>(); var current = new System.Text.StringBuilder(); var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (quoted) { if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; } else if (c == '"') quoted = false; else current.Append(c); }
                else if (c == '"') quoted = true;
                else if (c == separator) { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            fields.Add(current.ToString());
            rows.Add(fields.ToArray());
        }
        return rows;
    }

    // dd/mm/aaaa, d/m/aaaa, aaaa-mm-dd, or an Excel date serial number.
    private static DateTime? ParseDate(string text)
    {
        string[] formats = ["d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "yyyy-MM-dd", "d/M/yyyy H:mm:ss", "dd/MM/yyyy HH:mm:ss"];
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact)) return exact.Date;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 80000)
            return DateTime.FromOADate(serial).Date;
        return null;
    }

    // Empty or 0 = no amount. Accepts "1,234.56" and "1234.56"; rejects negatives and more than 2 decimals.
    private static long ParseAmount(string text, out bool error)
    {
        error = false;
        if (text.Length == 0) return 0;
        if (!decimal.TryParse(text.Replace(",", ""), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            || value < 0 || value > 9_999_999_999.99m) { error = true; return 0; }
        // Excel may store 1083440.91 as 1083440.9099999999: accept float residue, reject real extra decimals.
        var cents = decimal.Round(value, 2);
        if (Math.Abs(value - cents) > 0.000001m) { error = true; return 0; }
        value = cents;
        return (long)(value * 100);
    }
}
