using GestionLibros.Data;
using GestionLibros.Data.Import;
using GestionLibros.Models;
using GestionLibros.Reporting;

static class LedgerImportChecks
{
    public static async Task Run()
    {
        int count = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); count++; Console.WriteLine("PASS LEDGER IMPORT " + message); }
        async Task Denied(Func<Task> action, string expected)
        {
            try { await action(); } catch (InvalidOperationException e) { Check(e.Message.Contains(expected), "Explains limitation: " + expected); return; }
            throw new Exception("Expected rejection " + expected);
        }
        var db = new AppDatabase(Path.Combine(Path.GetTempPath(), $"fredi-ledger-import-{Guid.NewGuid():N}.db3"));
        await db.Setup("master", "Ledger-test-1234");
        var company = await TestSupport.NewCompany(db, "Ledger-test-1234", "Ledger imports");
        GlBalances Gl(long opening, long debit = 0, long credit = 0)
        { var d = new long[12]; var c = new long[12]; d[0] = debit; c[0] = credit; return new(opening, d, c); }
        await db.ImportAccounts(company, [
            new("1", "", 0, "Own totals", null, "", "", Gl(9000, 900)),
            new("1.1", "1", 1, "Cash", null, "", "", Gl(5000, 1000, 200)),
            new("2", "", 0, "Capital", null, "", ""),
            new("3", "", 0, "No own totals", null, "", ""),
            new("3.1", "3", 1, "Child", null, "", "", Gl(70)),
            new("10", "", 0, "Independent", null, "", "", Gl(400)),
            new("4", "", 0, "Zero net", null, "", "", Gl(0, 100, 100))
        ], 2026);
        var accounts = (await db.Accounts(company)).ToDictionary(x => x.Code);
        await db.SaveJournal(company, new Journal { Type = "01", Reference = "LOCAL", Date = new(2026, 1, 15), Concept = "Local" },
            [new() { AccountId = accounts["1.1"].Id, DebitCents = 300 }, new() { AccountId = accounts["2"].Id, CreditCents = 300 }]);
        Task<LedgerReport> Ledger(string code, DateTime start, DateTime end, bool children = true) => db.Ledger(company, accounts[code].Id, start, end, children);
        var ledger = await Ledger("1.1", new(2026, 1, 1), new(2026, 1, 31));
        Check(ledger.Opening == 5000 && ledger.Debits == 1300 && ledger.Credits == 200 && ledger.Closing == 6100, "50.00 opening plus imported and local activity");
        var balance = (await db.Balances(company, 2026, 1)).Single(x => x.Code == "1.1");
        var trial = (await db.TrialBalance(company, 2026, 1)).Rows.Single(x => x.Code == "1.1");
        var range = (await db.LedgerRange(company, 2026, 1, 1, "1.1", "1.1")).Accounts.Single();
        Check((balance.Opening, balance.Debits, balance.Credits, balance.Closing) == (ledger.Opening, ledger.Debits, ledger.Credits, ledger.Closing), "Consulta agrees");
        Check((trial.Opening, trial.Debits, trial.Credits, trial.Closing) == (ledger.Opening, ledger.Debits, ledger.Credits, ledger.Closing), "Balanza agrees");
        Check((range.Opening, range.Debits, range.Credits, range.Closing) == (ledger.Opening, ledger.Debits, ledger.Credits, ledger.Closing), "Auxiliares agrees");
        Check(ledger.Rows.Count == 2 && ledger.Rows.Single(x => x.IsImported).JournalId == 0 && ledger.Rows.Single(x => x.IsImported).LineId == 0, "Aggregate is not a journal");
        var doc = ReportFormatter.Ledger(ledger);
        Check(doc.Rows.Any(r => r.Cells[0].Value == "01/2026" && r.Cells[1].Value == "GL2000"), "Monthly aggregate date displayed without invented day");
        Check(ReportFormatter.Csv(doc).Contains("sin detalle diario") && ReportFormatter.Csv(doc).Contains("61.00"), "CSV includes limitation and balance");
        Check(ReportFormatter.Html(doc).Contains("01/2026"), "HTML includes month");
        Check(ReportFormatter.LedgerRange(await db.LedgerRange(company, 2026, 1, 1)).Rows.Any(r => r.Cells[2].Value == "01/2026"), "Auxiliary export uses month");
        var parent = await Ledger("1", new(2026, 1, 1), new(2026, 1, 31));
        Check(parent.Opening == 9000 && parent.Debits == 1200, "Own GL parent totals plus local descendant; no prefix double count");
        Check((await Ledger("1", new(2026, 1, 1), new(2026, 1, 31), false)).Debits == 900, "Direct parent excludes local descendants");
        Check((await Ledger("3", new(2026, 1, 1), new(2026, 1, 31))).Opening == 70, "Parent without own totals uses children");
        Check((await Ledger("3", new(2026, 1, 1), new(2026, 1, 31), false)).Opening == 0, "Direct parent excludes imported descendants");
        await Denied(() => Ledger("1.1", new(2026, 1, 2), new(2026, 1, 31)), "mes completo");
        await Denied(() => Ledger("1.1", new(2026, 1, 1), new(2026, 1, 30)), "mes completo");
        await Denied(() => Ledger("4", new(2026, 1, 1), new(2026, 1, 15)), "mes completo");
        await Denied(() => Ledger("1.1", new(2025, 12, 1), new(2026, 1, 31)), "ejercicio");
        var partial = await Ledger("1.1", new(2026, 2, 4), new(2026, 2, 20));
        Check(partial.Opening == 6100 && partial.Rows.Count == 0 && partial.HasImportedBalances, "Partial inactive month preserves known opening and historical notice");
        Check((await Ledger("1.1", new(2026, 1, 1), new(2026, 2, 28))).Closing == 6100, "Whole multimonth range supported");
        Check((await Ledger("1.1", new(2027, 1, 1), new(2027, 1, 31))).Opening == 6100, "Previous import year carried forward");
        Check((await Ledger("1.1", new(2025, 1, 1), new(2025, 1, 31))).Closing == 0, "Future imported year excluded");
        Check((await Ledger("2", new(2025, 12, 1), new(2026, 1, 31))).Closing == -300, "Local only cross-year range supported");
        Check((await Ledger("2", new(2026, 1, 10), new(2026, 1, 20))).Credits == 300, "Local exact daily range supported");
        await db.ImportAccounts(company, [new("1.1", "1", 1, "Cash", null, "", "", Gl(8000))], 2027);
        Check((await Ledger("1.1", new(2027, 1, 1), new(2027, 1, 31))).Opening == 8300, "Latest annual import replaces earlier imported amounts; local history preserved");
        Console.WriteLine($"All {count} ledger import checks passed.");
    }
}
