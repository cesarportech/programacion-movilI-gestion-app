using System.Text;
using GestionLibros.Models;

namespace GestionLibros.Data.Import;

// Importar / Exportar Catálogo CSV: Cuenta, Cuenta Superior, Descripción.
public static class CatalogCsv
{
    public static string Write(IEnumerable<Account> accounts)
    {
        static string Q(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";
        var output = new StringBuilder("\"Cuenta\",\"Cuenta Superior\",\"Descripcion\"\r\n");
        foreach (var a in accounts) output.Append(Q(a.Code)).Append(',').Append(Q(a.ParentCode)).Append(',').Append(Q(a.Description)).Append("\r\n");
        return output.ToString();
    }

    // Accepts the exported layout (header row optional, quoted fields, comma or semicolon separator).
    public static List<GlAccount> Parse(string text)
    {
        var result = new List<GlAccount>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0))
        {
            var fields = SplitCsv(line);
            if (fields.Count < 3) throw new FormatException($"Renglón con menos de 3 columnas: {line}");
            if (fields[0].Equals("Cuenta", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(new(fields[0].Trim(), fields[1].Trim(), -1, fields[2].Trim(), null, "", ""));
        }
        if (result.Count == 0) throw new FormatException("El archivo no tiene cuentas.");
        return result;
    }

    private static List<string> SplitCsv(string line)
    {
        var separator = line.Count(c => c == ';') > line.Count(c => c == ',') ? ';' : ',';
        var fields = new List<string>(); var current = new StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else current.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == separator) { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }
}
