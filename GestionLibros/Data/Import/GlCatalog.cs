using System.Text.RegularExpressions;

namespace GestionLibros.Data.Import;

public sealed record GlAccount(string Code, string ParentCode, int Level, string Description,
    DateTime? ChangedAt, string ChangedBy, string Status, GlBalances? Balances = null);

// Opening balance of the year (debit positive) and the 12 monthly debit/credit totals, in cents.
public sealed record GlBalances(long Opening, long[] Debits, long[] Credits)
{
    public bool IsZero => Opening == 0 && Debits.All(d => d == 0) && Credits.All(c => c == 0);
}

public sealed record GlCatalogFile(string Path, string Company, int? Year, int Accounts)
{
    public string Label => $"{Company} · {(Year?.ToString() ?? "plantilla")} · {Accounts} cuentas";
}

// Reads GL2000 account catalogs (CAT<empresa><año>.Tps) without modifying the source files.
public static partial class GlCatalog
{
    public const string DefaultRoot = @"C:\Sistemas\Datos";

    public static List<GlAccount> Read(string path)
    {
        // The old system may have the file open; share access and never write.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return Read(memory.ToArray());
    }

    public static List<GlAccount> Read(byte[] file)
    {
        var reader = new TpsReader(file);
        var table = reader.Tables.Values.FirstOrDefault(t => t.Fields.Any(f => f.Name == "CUENTA") && t.Fields.Any(f => f.Name == "DESC"))
            ?? throw new InvalidDataException("El archivo no contiene un catálogo de cuentas del GL2000.");
        var fields = table.Fields.Where(f => f.Elements <= 1).GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
        string Text(byte[] data, string name) => fields.TryGetValue(name, out var f) ? TpsReader.Value(f, data)?.ToString()?.Trim() ?? "" : "";
        // The GL stores amounts as REAL (double); round to cents like its screens do.
        long Cents(byte[] data, string name) => fields.TryGetValue(name, out var f) && TpsReader.Value(f, data) is double value
            ? (long)Math.Round((decimal)value * 100m, MidpointRounding.AwayFromZero) : 0;
        var hasBalances = fields.ContainsKey("SAL_INI") && Months.All(m => fields.ContainsKey("CAR_" + m) && fields.ContainsKey("CRE_" + m));
        var result = new List<GlAccount>();
        foreach (var row in reader.Rows.Where(r => r.Table == table.Number))
        {
            var code = Text(row.Data, "CUENTA");
            if (code.Length == 0) continue;
            var changed = fields.TryGetValue("FECHACAMBIO", out var date) && TpsReader.Value(date, row.Data) is int days ? TpsReader.ClarionDate(days) : null;
            result.Add(new(code, Text(row.Data, "SCTA"), int.TryParse(Text(row.Data, "NIVEL"), out var level) ? level : -1,
                Text(row.Data, "DESC"), changed, Text(row.Data, "USUARIO"), Text(row.Data, "STATUS"),
                hasBalances ? new GlBalances(Cents(row.Data, "SAL_INI"), Months.Select(m => Cents(row.Data, "CAR_" + m)).ToArray(),
                    Months.Select(m => Cents(row.Data, "CRE_" + m)).ToArray()) : null));
        }
        return result;
    }

    private static readonly string[] Months = ["ENE", "FEB", "MAR", "ABR", "MAY", "JUN", "JUL", "AGO", "SEP", "OCT", "NOV", "DIC"];

    // CAT00326.Tps → 2026. Templates (CATEMP…) and unknown names have no fiscal year.
    public static int? YearOf(string path)
    {
        var match = CatalogName().Match(System.IO.Path.GetFileName(path));
        if (!match.Success || match.Groups["company"].Value.Equals("EMP", StringComparison.OrdinalIgnoreCase)) return null;
        return 2000 + int.Parse(match.Groups["year"].Value);
    }

    [GeneratedRegex(@"^cat(?<company>.+?)(?<year>\d{2})\.tps$", RegexOptions.IgnoreCase)]
    private static partial Regex CatalogName();

    // Lists every readable, non-empty catalog under the GL2000 data folder (one subfolder per company).
    public static List<GlCatalogFile> Find(string root = DefaultRoot)
    {
        var found = new List<GlCatalogFile>();
        if (!Directory.Exists(root)) return found;
        foreach (var path in Directory.EnumerateFiles(root, "cat*.tps", SearchOption.AllDirectories))
        {
            var match = CatalogName().Match(System.IO.Path.GetFileName(path));
            if (!match.Success) continue;
            var company = match.Groups["company"].Value;
            var year = YearOf(path);
            try
            {
                var count = Read(path).Count;
                if (count > 0) found.Add(new(path, company.ToUpperInvariant(), year, count));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { }
        }
        return found.OrderBy(f => f.Year == null).ThenBy(f => f.Company).ThenByDescending(f => f.Year).ToList();
    }
}
