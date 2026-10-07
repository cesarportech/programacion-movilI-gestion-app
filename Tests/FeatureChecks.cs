using GestionLibros.Data;
using GestionLibros.Data.Import;
using GestionLibros.Models;
using GestionLibros.Reporting;

// Tipos de Pólizas, Duplicar, Impresión de Pólizas, Balance General, Estado de Resultados, CSV and backup.
static class FeatureChecks
{
    static int passed;
    static void Check(bool condition, string description) { if (!condition) throw new Exception(description); passed++; Console.WriteLine("PASS FEATURE " + description); }
    static async Task Denied(Func<Task> action, string description)
    {
        try { await action(); } catch (InvalidOperationException) { passed++; Console.WriteLine("PASS FEATURE " + description); return; }
        throw new Exception("Expected denial: " + description);
    }

    public static async Task Run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fredi-features-{Guid.NewGuid():N}.db3");
        var db = new AppDatabase(path);
        await db.Setup("master", "Feature-test-1234");
        var c = await TestSupport.NewCompany(db, "Feature-test-1234", "Empresa F");
        foreach (var (code, parent, name) in new[] { ("1", "", "ACTIVO"), ("1.1", "1", "Caja"), ("2", "", "PASIVO"), ("2.1", "2", "Proveedores"),
            ("3", "", "CAPITAL"), ("3.1", "3", "Capital social"), ("4", "", "INGRESOS"), ("4.1", "4", "Ventas"), ("5", "", "GASTOS"), ("5.1", "5", "Sueldos") })
            await db.AddAccount(c, code, name, parent);
        var ids = (await db.Accounts(c)).ToDictionary(a => a.Code, a => a.Id);
        async Task Post(string reference, DateTime date, string debit, string credit, long cents) =>
            await db.SaveJournal(c, new Journal { Type = "01", Reference = reference, Date = date, Concept = "Prueba " + reference },
                [new() { AccountId = ids[debit], DebitCents = cents }, new() { AccountId = ids[credit], CreditCents = cents }]);
        await Post("1", new DateTime(2026, 1, 5), "1.1", "3.1", 100000);   // capital aportado
        await Post("2", new DateTime(2026, 1, 20), "1.1", "4.1", 30000);   // venta enero
        await Post("3", new DateTime(2026, 2, 10), "1.1", "4.1", 50000);   // venta febrero
        await Post("4", new DateTime(2026, 2, 15), "5.1", "2.1", 20000);   // sueldo por pagar

        await db.SavePolicyType(c, "01", "INGRESOS");
        await db.SavePolicyType(c, "01", "Ingresos y ventas");
        await db.SavePolicyType(c, "02", "EGRESOS");
        var types = await db.PolicyTypes(c);
        Check(types.Count == 2 && types[0].Description == "Ingresos y ventas", "Policy type saved once and description updated");
        await Denied(() => db.DeletePolicyType(c, "01"), "Policy type in use cannot be deleted");
        await db.DeletePolicyType(c, "02");
        Check((await db.PolicyTypes(c)).Count == 1, "Unused policy type deleted");
        await Denied(() => db.SavePolicyType(c, "", "Sin código"), "Policy type requires a code");

        var february = (await db.Journals(c, 2026, 2)).Single(j => j.Reference == "3");
        await db.DuplicateJournal(c, february.Id, "3B", february.Date);
        var copy = (await db.Journals(c, 2026, 2)).Single(j => j.Reference == "3B");
        var copyLines = await db.Lines(c, copy.Id);
        Check(copy.Concept == february.Concept && copyLines.Count == 2 && copyLines.Sum(l => l.DebitCents) == 50000, "Duplicated póliza keeps concept and movements");
        await Denied(() => db.DuplicateJournal(c, february.Id, "3", february.Date), "Duplicate with an existing reference is rejected");
        await db.DeleteJournal(c, copy.Id);

        var printed = await db.PoliciesForPrint(c, 2026, 2, 2);
        Check(printed.Count == 2 && printed.All(p => p.TypeName == "Ingresos y ventas") && printed[0].Lines.Count == 2, "Pólizas of the period print with type name and movements");
        Check((await db.PoliciesForPrint(c, 2026, 1, 2, "99")).Count == 0, "Type filter applies to printed pólizas");
        var policyDoc = ReportFormatter.Policies("Empresa F", 2026, 2, 2, printed);
        Check(policyDoc.Rows.Count(r => r.Summary && r.Cells[2].Value == "Sumas Iguales :") == 2, "Each printed póliza ends with Sumas Iguales");

        var balance = ReportFormatter.BalanceSheet(await db.TrialBalance(c, 2026, 2, 9, false));
        Check(balance.Note.Contains("Activo igual a Pasivo más Capital"), "Balance General balances assets against liabilities, capital and result");
        Check(balance.Rows.Any(r => r.Cells[1].Value == "Utilidad del Ejercicio" && r.Cells[2].Value == "600.00"), "Balance General shows the year result (80,000 − 20,000 cents)");
        var income = await db.IncomeStatement(c, 2026, 2);
        var sales = income.Rows.Single(r => r.Code == "4.1");
        Check(sales.Month == -50000 && sales.YearToDate == -80000, "Estado de Resultados separates month and year to date");
        var incomeDoc = ReportFormatter.IncomeStatement(income);
        Check(incomeDoc.Rows.Last().Cells[1].Value == "UTILIDAD DEL EJERCICIO" && incomeDoc.Rows.Last().Cells[2].Value == "300.00" && incomeDoc.Rows.Last().Cells[3].Value == "600.00",
            "Estado de Resultados result: month 300.00, year 600.00");

        var csv = CatalogCsv.Write(await db.Accounts(c));
        var parsed = CatalogCsv.Parse(csv);
        Check(parsed.Count == 10 && parsed.Single(a => a.Code == "1.1").ParentCode == "1", "Catalog CSV round-trips codes and parents");
        Check(CatalogCsv.Parse("\"9\";\"\";\"Cuenta; con punto y coma\"").Single().Description == "Cuenta; con punto y coma", "Catalog CSV accepts quoted semicolon files");
        var other = await TestSupport.NewCompany(db, "Feature-test-1234", "Empresa CSV");
        Check((await db.ImportAccounts(other, parsed)).Added == 10, "Catalog CSV imports into another company");

        await db.Login("master", "Feature-test-1234");
        var backup = await db.Backup(Path.Combine(Path.GetTempPath(), $"fredi-backup-{Guid.NewGuid():N}"));
        var restored = new AppDatabase(backup);
        await TestSupport.LoginAs(restored, c);
        Check((await restored.Accounts(c)).Count == 10, "Backup copy opens with the same data");
        Check((await db.ActiveSessions()).Count == 0, "No remembered sessions before issuing a token");
        await db.IssueToken();
        Check((await db.ActiveSessions()).Single().Username == "master", "Usuarios Conectados lists remembered sessions");
        // Empresas y Usuarios: what the master can do; operators cannot.
        var users = await db.UserList();
        Check(users[0].IsMaster && users.Any(u => u.Username == TestSupport.UserFor(c) && u.CompanyName == "Empresa F"), "Master lists users with their company");
        var operatorF = users.Single(u => u.Username == TestSupport.UserFor(c));
        await db.SetUserPassword(operatorF.Id, "Cambiada-por-maestro-1");
        await Denied(() => db.SetUserPassword(operatorF.Id, "corta"), "Master password change keeps the 10-character minimum");
        await Denied(() => db.DeleteUser(users.Single(u => u.IsMaster).Id), "Master user cannot be deleted");
        await db.DeleteUser(users.Single(u => u.Username == TestSupport.UserFor(other)).Id);
        await Denied(() => db.Login(TestSupport.UserFor(other), TestSupport.OperatorPassword), "Deleted user cannot sign in");
        await db.Login(TestSupport.UserFor(c), "Cambiada-por-maestro-1");
        Check(db.Session?.CompanyId == c, "Operator signs in with the password set by the master");
        await Denied(async () => { await db.UserList(); }, "Operator cannot list users");
        await Denied(async () => { await db.DeleteUser(operatorF.Id); }, "Operator cannot delete users");
        Console.WriteLine($"All {passed} feature checks passed.");
    }
}
