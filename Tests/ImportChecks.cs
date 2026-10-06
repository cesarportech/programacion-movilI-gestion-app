using GestionLibros.Data;
using GestionLibros.Data.Import;

record BalanceRowView(long Opening, long Debits, long Credits, long Closing);

static class ImportChecks
{
    static int passed;
    static void Check(bool condition, string description) { if (!condition) throw new Exception(description); passed++; Console.WriteLine("PASS IMPORT " + description); }
    static async Task Denied(Func<Task> action, string description)
    {
        try { await action(); } catch (InvalidOperationException) { passed++; Console.WriteLine("PASS IMPORT " + description); return; }
        throw new Exception("Expected denial: " + description);
    }

    public static async Task Run(AppDatabase db, int postedCompany, string postedParent)
    {
        await db.Login("master", "Test-only-secret-123");
        await db.AddCompany("Empresa Importada");
        var target = (await db.Companies()).Single(c => c.Name == "Empresa Importada").Id;
        var changed = new DateTime(2026, 2, 26);
        // Children listed before parents, like an unordered TPS file.
        List<GlAccount> catalog =
        [
            new("1.11101", "1.11100", 3, "Caja Chica Administración", null, "", ""),
            new("1.11100", "1.10000", 2, "Caja Chica", changed, "iasc", "C"),
            new("1.10000", "1.00000", 1, "ACTIVOS CORRIENTES", null, "", ""),
            new("1.00000", "", 0, "ACTIVOS", null, "", ""),
        ];
        var first = await db.ImportAccounts(target, catalog);
        Check(first == new ImportResult(4, 0), "Unordered catalog imports all accounts");
        var accounts = (await db.Accounts(target)).ToDictionary(a => a.Code);
        Check(accounts["1.00000"].Level == 1 && accounts["1.11101"].Level == 4, "Levels follow the parent chain");
        Check(accounts["1.11100"].ChangedBy == "iasc" && accounts["1.11100"].Status == "C" && accounts["1.11100"].ChangedAt == changed, "Change date, user and status preserved");

        await db.UpdateAccount(target, accounts["1.00000"].Id, "ACTIVO TOTAL");
        var again = await db.ImportAccounts(target, catalog);
        Check(again == new ImportResult(0, 4), "Repeated import skips existing accounts");
        Check((await db.Accounts(target)).Single(a => a.Code == "1.00000").Description == "ACTIVO TOTAL", "Repeated import does not overwrite local edits");
        Check((await db.Accounts(target)).Single(a => a.Code == "1.00000").Status == "C", "Local edit marks status C");

        await Denied(() => db.ImportAccounts(target, [new("8", "", 0, "Valida", null, "", ""), new("9.1", "9", 1, "Huérfana", null, "", "")]), "Missing parent rejected");
        Check((await db.Accounts(target)).All(a => a.Code != "8"), "Rejected import rolls back every account");
        await Denied(() => db.ImportAccounts(target, [new("7", "", 0, "Uno", null, "", ""), new("7", "", 0, "Dos", null, "", "")]), "Duplicate code in source rejected");
        await Denied(() => db.ImportAccounts(postedCompany, [new("9.9.9", postedParent, 2, "Hija", null, "", "")]), "Child under posted account rejected");

        // GL accumulated amounts: opening of the year plus monthly debits/credits on detail accounts.
        await db.AddCompany("Empresa Saldos");
        var sums = (await db.Companies()).Single(c => c.Name == "Empresa Saldos").Id;
        GlBalances Gl(long opening, params (int Month, long Debit, long Credit)[] months)
        {
            var debits = new long[12]; var credits = new long[12];
            foreach (var (m, d, c) in months) { debits[m - 1] = d; credits[m - 1] = c; }
            return new(opening, debits, credits);
        }
        List<GlAccount> withBalances =
        [
            // Like the GL, this parent's stored totals differ from the sum of its children.
            new("1", "", 0, "ACTIVO", null, "", "", Gl(5000, (2, 999, 0))),
            new("1.1", "1", 1, "Caja", null, "", "", Gl(1000, (1, 200, 50), (2, 300, 0), (3, 0, 100))),
            new("1.2", "1", 1, "Proveedor", null, "", "", Gl(-400, (2, 0, 25))),
            new("2", "", 0, "Vacía", null, "", "", Gl(0)),
            new("3", "", 0, "Sin totales propios", null, "", "", Gl(0)),
            new("3.1", "3", 1, "Hija", null, "", "", Gl(70)),
        ];
        var imported = await db.ImportAccounts(sums, withBalances, 2026);
        Check(imported.BalanceAccounts == 4, "Only accounts with non-zero GL amounts carry imported rows");
        async Task<BalanceRowView> At(int year, int month, string code)
        { var r = (await db.Balances(sums, year, month)).Single(x => x.Code == code); return new(r.Opening, r.Debits, r.Credits, r.Closing); }
        Check(await At(2026, 2, "1.1") == new BalanceRowView(1150, 300, 0, 1450), "February opening adds January, period shows February");
        Check(await At(2026, 3, "1.1") == new BalanceRowView(1450, 0, 100, 1350), "March opening adds January and February");
        Check(await At(2026, 2, "1") == new BalanceRowView(5000, 999, 0, 5999), "Parent shows its own GL totals, as the GL screen does");
        Check(await At(2026, 2, "3") == new BalanceRowView(70, 0, 0, 70), "Parent without GL totals adds up its children");
        Check(await At(2027, 1, "1.1") == new BalanceRowView(1350, 0, 0, 1350), "Later year carries the whole imported year as opening");
        Check(await At(2025, 12, "1.1") == new BalanceRowView(0, 0, 0, 0), "Earlier year has no imported amounts");
        withBalances[1] = withBalances[1] with { Balances = Gl(2000) };
        await db.ImportAccounts(sums, withBalances, 2026);
        Check(await At(2026, 2, "1.1") == new BalanceRowView(2000, 0, 0, 2000), "Re-import replaces the year's amounts instead of adding them");
        var cashId = (await db.Accounts(sums)).Single(a => a.Code == "1.1").Id;
        await Denied(() => db.DeleteAccount(sums, cashId), "Account with imported amounts cannot be deleted");

        await db.Login("operador", "Operator-secret-123");
        await Denied(() => db.ImportAccounts(target, catalog), "Operator cannot import into another company");

        // Optional check against the real GL2000 file, only read and only when present on this machine.
        const string real = @"C:\Sistemas\Datos\003\Cat00326.Tps";
        if (File.Exists(real))
        {
            var before = File.GetLastWriteTimeUtc(real);
            var rows = GlCatalog.Read(real);
            Check(rows.Count > 0 && rows.Select(r => r.Code).Distinct().Count() == rows.Count, "Real GL2000 catalog read with unique codes");
            Check(rows.All(r => r.ParentCode.Length == 0 || rows.Any(p => p.Code == r.ParentCode)), "Real GL2000 catalog has every parent");
            // The Consulta for September must show exactly the GL's own totals for the root account.
            await db.Login("master", "Test-only-secret-123");
            await db.AddCompany("Copia GL 003");
            var copy = (await db.Companies()).Single(c => c.Name == "Copia GL 003").Id;
            await db.ImportAccounts(copy, rows, GlCatalog.YearOf(real));
            var root = rows.First(r => r.ParentCode.Length == 0).Balances!;
            var glOpening = root.Opening + root.Debits.Take(8).Sum() - root.Credits.Take(8).Sum();
            var fredi = (await db.Balances(copy, 2026, 9)).Single(b => b.Code == rows.First(r => r.ParentCode.Length == 0).Code);
            Console.WriteLine($"      GL {glOpening} {root.Debits[8]} {root.Credits[8]} | FREDI {fredi.Opening} {fredi.Debits} {fredi.Credits}");
            Check(fredi.Opening == glOpening && fredi.Debits == root.Debits[8] && fredi.Credits == root.Credits[8],
                "Real catalog September consulta matches the GL exactly");
            Check(File.GetLastWriteTimeUtc(real) == before, "Real GL2000 file left unmodified");
            var previewDirectory = Environment.GetEnvironmentVariable("FREDI_REPORT_PREVIEW_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(previewDirectory))
            {
                Directory.CreateDirectory(previewDirectory);
                var october = GestionLibros.Reporting.ReportFormatter.TrialBalance(await db.TrialBalance(copy, 2026, 10, 9, false));
                await File.WriteAllTextAsync(Path.Combine(previewDirectory, "balanza-gl.html"), GestionLibros.Reporting.ReportFormatter.Html(october, false, false));
                var mayor = GestionLibros.Reporting.ReportFormatter.LedgerRange(await db.LedgerRange(copy, 2026, 10, 10, "1.11102", "1.14101"));
                await File.WriteAllTextAsync(Path.Combine(previewDirectory, "mayor-gl.html"), GestionLibros.Reporting.ReportFormatter.Html(mayor, false, false));
                var saldos = GestionLibros.Reporting.ReportFormatter.AccountBalances(await db.TrialBalance(copy, 2026, 10, 9, false), "1.12000", "1.12999");
                await File.WriteAllTextAsync(Path.Combine(previewDirectory, "saldos-gl.html"), GestionLibros.Reporting.ReportFormatter.Html(saldos, false, false));
            }
        }
        else Console.WriteLine("SKIP IMPORT real GL2000 catalog not present");
        Console.WriteLine($"All {passed} import checks passed.");
    }
}
