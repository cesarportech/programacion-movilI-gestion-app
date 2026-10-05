using GestionLibros.Data;
using GestionLibros.Models;
using GestionLibros.Reporting;
using System.Globalization;

internal static class ReportingChecks
{
    public static async Task Run()
    {
        var count = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            count++;
            Console.WriteLine("PASS REPORT " + message);
        }
        async Task Denied(Func<Task> action, string message)
        {
            try { await action(); }
            catch (InvalidOperationException) { count++; Console.WriteLine("PASS REPORT " + message); return; }
            throw new Exception("Expected rejection: " + message);
        }
        var database = new AppDatabase(Path.Combine(Path.GetTempPath(), $"fredi-reports-{Guid.NewGuid():N}.db3"));
        await database.Setup("master", "Report-test-1234");
        await database.Login("master", "Report-test-1234");
        await database.AddCompany("Empresa Á <prueba>"); await database.AddCompany("Empresa B");
        var companies = await database.Companies();
        var a = companies.Single(c => c.Name.StartsWith("Empresa Á")).Id;
        var b = companies.Single(c => c.Name == "Empresa B").Id;
        await database.AddAccount(a, "1", "Activos", "");
        await database.AddAccount(a, "1.01", "Caja", "1");
        await database.AddAccount(a, "1.02", "Banco", "1");
        await database.AddAccount(a, "2", "Capital", "");
        await database.AddAccount(a, "3", "Sin actividad", "");
        // Similar prefix must not make this account a child of 1.
        await database.AddAccount(a, "10", "Cuenta independiente", "");
        await database.AddAccount(b, "1", "Solo B", "");
        var accounts = await database.Accounts(a);
        var root = accounts.Single(x => x.Code == "1").Id;
        var cash = accounts.Single(x => x.Code == "1.01").Id;
        var bank = accounts.Single(x => x.Code == "1.02").Id;
        var capital = accounts.Single(x => x.Code == "2").Id;
        var separate = accounts.Single(x => x.Code == "10").Id;
        async Task Save(string reference, DateTime date, int debit, int credit, long amount, string concept = "Prueba") =>
            await database.SaveJournal(a, new Journal { Type = "01", Reference = reference, Date = date, Concept = concept },
                new[] { new JournalLine { AccountId = debit, DebitCents = amount }, new JournalLine { AccountId = credit, CreditCents = amount } });
        await Save("01", new DateTime(2025, 12, 31), cash, capital, 10001);
        await Save("02", new DateTime(2026, 1, 1), cash, capital, 2034);
        await Save("03", new DateTime(2026, 1, 31), bank, cash, 500);
        await Save("04", new DateTime(2026, 1, 31), separate, capital, 100);
        await Save("05", new DateTime(2026, 2, 1), cash, capital, 999);
        var trial = await database.TrialBalance(a, 2026, 1);
        Check(trial.Totals.IsBalanced, "Trial balance equals on all three pairs");
        Check(trial.Totals.OpeningDebit == 10001 && trial.Totals.OpeningCredit == 10001, "Opening carries previous year");
        Check(trial.Totals.Debits == 2634 && trial.Totals.Credits == 2634, "Totals count detail once, exclude future month");
        Check(trial.Totals.ClosingDebit == 12135 && trial.Totals.ClosingCredit == 12135, "Closing balances match exact cents");
        var levelOne = await database.TrialBalance(a, 2026, 1, 1);
        Check(levelOne.Rows.All(r => r.Level <= 1) && levelOne.Totals == trial.Totals, "Level filter preserves general totals");
        var active = await database.TrialBalance(a, 2026, 1, 9, false);
        Check(active.Rows.All(r => r.Code != "3") && active.Totals == trial.Totals, "Zero filter excludes inactive accounts only");
        var empty = await database.TrialBalance(b, 2026, 1);
        Check(empty.Totals.Debits == 0 && empty.Rows.Count == 1, "Other company has no leaked movements");
        var ledger = await database.Ledger(a, cash, new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        Check(ledger.Opening == 10001 && ledger.Rows.Count == 2, "Ledger opening and inclusive boundaries");
        Check(ledger.Rows[0].Balance == 12035 && ledger.Rows[1].Balance == 11535, "Ledger running balance follows debit and credit");
        Check(ledger.Closing == trial.Rows.Single(r => r.Code == "1.01").Closing, "Ledger reconciles with trial balance");
        Check(ledger.Rows[0].Concept == "Prueba", "Blank movement concept falls back to journal concept");
        var parent = await database.Ledger(a, root, new DateTime(2026, 1, 1), new DateTime(2026, 1, 31));
        Check(parent.Rows.Count == 3 && parent.Closing == 12035, "Parent includes real descendants, not prefix lookalikes");
        var direct = await database.Ledger(a, root, new DateTime(2026, 1, 1), new DateTime(2026, 1, 31), false);
        Check(direct.Rows.Count == 0 && direct.Opening == 0, "Parent direct-only option excludes descendants");
        var idle = await database.Ledger(a, cash, new DateTime(2026, 3, 1), new DateTime(2026, 3, 31));
        Check(idle.Rows.Count == 0 && idle.Opening == idle.Closing && idle.Opening == 12534, "Inactive period retains previous balance");
        await Denied(async () => { await database.TrialBalance(a, 2026, 1, 10); }, "Invalid level rejected");
        await Denied(async () => { await database.TrialBalance(a, 2026, 13); }, "Invalid month rejected");
        await Denied(async () => { await database.Ledger(a, cash, new DateTime(2026, 2, 1), new DateTime(2026, 1, 1)); }, "Reversed dates rejected");
        var foreign = (await database.Accounts(b)).Single().Id;
        await Denied(async () => { await database.Ledger(a, foreign, new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)); }, "Forged account ID rejected");
        await database.AddUser("operator", "Operator-test-1234", a);
        await database.Login("operator", "Operator-test-1234");
        await Denied(async () => { await database.TrialBalance(b, 2026, 1); }, "Operator cannot read foreign trial balance");
        await Denied(async () => { await database.Ledger(b, foreign, new DateTime(2026, 1, 1), new DateTime(2026, 1, 31)); }, "Operator cannot read foreign ledger");
        database.Logout();
        await Denied(async () => { await database.TrialBalance(a, 2026, 1); }, "Logged-out report rejected");
        var document = ReportFormatter.TrialBalance(trial);
        Check(document.Headers.Count == 8 && document.Rows.Last().Summary, "Trial export has headers and general totals");
        var csv = ReportFormatter.Csv(document);
        Check(csv.Contains("100.01") && csv.Contains("Empresa Á <prueba>"), "CSV keeps precise decimals and Unicode");
        var html = ReportFormatter.Html(document, true);
        Check(html.Contains("Empresa Á &lt;prueba&gt;") || html.Contains("Empresa &#193; &lt;prueba&gt;"), "HTML encodes company text");
        Check(html.Contains("@media print") && html.Contains("thead{display:table-header-group}"), "HTML contains print pagination styles");
        var hostile = new ReportDocument("<script>alert(1)</script>", "=HYPERLINK(1)", "2026", "",
            new[] { "Text", "Number" }, new[] { new ReportRow(new[] { ReportCell.Text("  +SUM(1,2)"), ReportCell.Money(-123) }),
                new ReportRow(new[] { ReportCell.Text("Texto, con \"comillas\"\ny salto"), ReportCell.Money(0) }) }, "");
        var escaped = ReportFormatter.Csv(hostile);
        Check(escaped.Contains("'=HYPERLINK(1)") && escaped.Contains("'  +SUM(1,2)"), "CSV neutralizes formulas in text fields");
        Check(escaped.Contains("\"-1.23\""), "Negative numeric amounts remain numbers");
        Check(escaped.Contains("\"\"comillas\"\""), "CSV quotes embedded commas, quotes and newlines");
        Check(!ReportFormatter.Html(hostile).Contains("<script>alert"), "HTML does not execute stored markup");
        Check(!ReportFormatter.Html(document, false, false).Contains("window.print()"), "Embedded preview omits browser-only print action");
        var ledgerDoc = ReportFormatter.Ledger(ledger);
        Check(ledgerDoc.Rows.Count == 4 && ledgerDoc.Rows.First().Summary && ledgerDoc.Rows.Last().Summary, "Ledger export includes opening and totals");
        var priorCulture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-ES"); Check(ReportFormatter.Csv(document) == csv, "CSV decimals independent of workstation culture"); }
        finally { CultureInfo.CurrentCulture = priorCulture; }
        var previewDirectory = Environment.GetEnvironmentVariable("FREDI_REPORT_PREVIEW_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(previewDirectory))
        {
            Directory.CreateDirectory(previewDirectory);
            await File.WriteAllTextAsync(Path.Combine(previewDirectory, "balanza.html"), ReportFormatter.Html(document));
            await File.WriteAllTextAsync(Path.Combine(previewDirectory, "mayor.html"), ReportFormatter.Html(ledgerDoc));
            var longReport = document with { Rows = Enumerable.Range(0, 30).SelectMany(_ => document.Rows).ToList() };
            await File.WriteAllTextAsync(Path.Combine(previewDirectory, "balanza-larga.html"), ReportFormatter.Html(longReport, true));
        }
        Console.WriteLine($"All {count} reporting checks passed.");
    }
}
