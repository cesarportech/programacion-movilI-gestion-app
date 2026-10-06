using GestionLibros.Models;

namespace GestionLibros.Data;

public sealed partial class AppDatabase
{
    // SQLite maps the joined columns into this read model; it is not a stored table.
    public sealed class PostedLine
    {
        public int JournalId { get; set; }
        public int LineId { get; set; }
        public int AccountId { get; set; }
        public DateTime Date { get; set; }
        public string Type { get; set; } = "";
        public string Reference { get; set; } = "";
        public string Concept { get; set; } = "";
        public long DebitCents { get; set; }
        public long CreditCents { get; set; }
    }

    private record ReportSnapshot(string CompanyName, List<Account> Accounts, List<PostedLine> Lines, List<ImportedBalance> Imported);

    private async Task<ReportSnapshot> ReadReportSnapshot(int companyId, DateTime before)
    {
        RequireCompany(companyId);
        ReportSnapshot? snapshot = null;
        // One transaction keeps the account tree, headers and movements consistent.
        await db.RunInTransactionAsync(connection =>
        {
            var company = connection.Find<Company>(companyId)
                ?? throw new InvalidOperationException("Contabilidad no disponible.");
            var accounts = connection.Table<Account>().Where(a => a.CompanyId == companyId)
                .OrderBy(a => a.Code).ToList();
            var lines = connection.Query<PostedLine>(
                "SELECT j.Id AS JournalId, l.Id AS LineId, l.AccountId, j.Date, j.Type, j.Reference, " +
                "CASE WHEN l.Concept = '' THEN j.Concept ELSE l.Concept END AS Concept, " +
                "l.DebitCents, l.CreditCents FROM JournalLine l INNER JOIN Journal j ON j.Id = l.JournalId " +
                "INNER JOIN Account a ON a.Id = l.AccountId " +
                "WHERE j.CompanyId = ? AND a.CompanyId = ? AND j.Date < ?",
                companyId, companyId, before);
            var imported = connection.Table<ImportedBalance>().Where(b => b.CompanyId == companyId).ToList();
            snapshot = new(company.Name, accounts, lines, imported);
        });
        return snapshot!;
    }

    private static DateTime PeriodStart(int year, int month)
    {
        if (year is < 1900 or > 2100 || month is < 1 or > 12)
            throw new InvalidOperationException("Seleccione un período válido entre 1900 y 2100.");
        return new DateTime(year, month, 1);
    }

    private static List<BalanceRow> BuildBalances(ReportSnapshot snapshot, DateTime start)
    {
        var totals = snapshot.Accounts.ToDictionary(a => a.Id, _ => new long[3]);
        foreach (var line in snapshot.Lines)
        {
            var values = totals[line.AccountId];
            if (line.Date < start)
                values[0] = checked(values[0] + line.DebitCents - line.CreditCents);
            else
            {
                values[1] = checked(values[1] + line.DebitCents);
                values[2] = checked(values[2] + line.CreditCents);
            }
        }
        var imported = ImportedTotals(snapshot, start);
        // A parent with its own GL totals shows them as the GL does; only parents without imported
        // amounts (e.g. created in FREDI) add up their children's imported amounts.
        var ownImported = imported.Keys.ToHashSet();
        var byCode = snapshot.Accounts.ToDictionary(a => a.Code);
        foreach (var account in snapshot.Accounts.OrderByDescending(a => a.Level))
        {
            if (!byCode.TryGetValue(account.ParentCode, out var parent)) continue;
            for (var i = 0; i < 3; i++)
                totals[parent.Id][i] = checked(totals[parent.Id][i] + totals[account.Id][i]);
            if (ownImported.Contains(parent.Id) || !imported.TryGetValue(account.Id, out var child)) continue;
            if (!imported.TryGetValue(parent.Id, out var sum)) imported[parent.Id] = sum = new long[3];
            for (var i = 0; i < 3; i++) sum[i] = checked(sum[i] + child[i]);
        }
        var parentCodes = snapshot.Accounts.Select(a => a.ParentCode).ToHashSet();
        return snapshot.Accounts.Select(a =>
        {
            var own = imported.GetValueOrDefault(a.Id) ?? new long[3];
            return new BalanceRow(a.Code, a.Description, a.Level, checked(totals[a.Id][0] + own[0]),
                checked(totals[a.Id][1] + own[1]), checked(totals[a.Id][2] + own[2]), !parentCodes.Contains(a.Code));
        }).ToList();
    }

    // GL2000 amounts: each year's opening already contains all earlier years, so only the latest
    // imported year at or before the period is used. Opening = year opening + months before the period;
    // the period month supplies cargos and créditos.
    private static Dictionary<int, long[]> ImportedTotals(ReportSnapshot snapshot, DateTime start)
    {
        var result = new Dictionary<int, long[]>();
        foreach (var account in snapshot.Imported.GroupBy(b => b.AccountId))
        {
            var years = account.Where(b => b.Year <= start.Year).Select(b => b.Year).ToList();
            if (years.Count == 0) continue;
            var year = years.Max();
            var values = result[account.Key] = new long[3];
            foreach (var b in account.Where(b => b.Year == year))
            {
                if (year < start.Year || b.Month < start.Month)
                    values[0] = checked(values[0] + b.DebitCents - b.CreditCents);
                else if (b.Month == start.Month)
                {
                    values[1] = checked(values[1] + b.DebitCents);
                    values[2] = checked(values[2] + b.CreditCents);
                }
            }
        }
        return result;
    }

    public async Task<TrialBalanceReport> TrialBalance(int companyId, int year, int month,
        int maximumLevel = 9, bool includeZeroAccounts = true)
    {
        RequireCompany(companyId);
        if (maximumLevel is < 1 or > 9)
            throw new InvalidOperationException("El nivel máximo debe estar entre 1 y 9.");
        var start = PeriodStart(year, month);
        var snapshot = await ReadReportSnapshot(companyId, start.AddMonths(1));
        var balances = BuildBalances(snapshot, start);
        // Only detail accounts contribute to totals, regardless of the visible level.
        var detail = balances.Where(b => b.IsDetail).ToList();
        var totals = new TrialBalanceTotals(
            detail.Sum(b => Math.Max(0, b.Opening)), detail.Sum(b => Math.Max(0, -b.Opening)),
            detail.Sum(b => b.Debits), detail.Sum(b => b.Credits),
            detail.Sum(b => Math.Max(0, b.Closing)), detail.Sum(b => Math.Max(0, -b.Closing)));
        var visible = balances.Where(b => b.Level <= maximumLevel &&
            (includeZeroAccounts || b.Opening != 0 || b.Debits != 0 || b.Credits != 0 || b.Closing != 0)).ToList();
        return new(snapshot.CompanyName, year, month, maximumLevel, includeZeroAccounts, visible, totals);
    }

    // Result accounts (every root whose code does not start with 1, 2 or 3: ingresos, costos, gastos):
    // movement of the month and accumulated from January through the month.
    public async Task<IncomeStatementReport> IncomeStatement(int companyId, int year, int month, int maximumLevel = 9)
    {
        RequireCompany(companyId);
        if (maximumLevel is < 1 or > 9) throw new InvalidOperationException("El nivel máximo debe estar entre 1 y 9.");
        var start = PeriodStart(year, month);
        var snapshot = await ReadReportSnapshot(companyId, start.AddMonths(1));
        var monthRows = BuildBalances(snapshot, start);
        var yearOpening = BuildBalances(snapshot, new DateTime(year, 1, 1)).ToDictionary(b => b.Code, b => b.Opening);
        var rows = monthRows.Where(b => IsResultAccount(b.Code) && b.Level <= maximumLevel)
            .Select(b => new IncomeRow(b.Code, b.Description, b.Level, b.IsDetail, checked(b.Debits - b.Credits),
                checked(b.Closing - yearOpening[b.Code])))
            .Where(r => r.Month != 0 || r.YearToDate != 0).ToList();
        return new(snapshot.CompanyName, year, month, maximumLevel, rows);
    }

    // GL charts start assets with 1, liabilities with 2 and capital with 3; the rest are result accounts.
    public static bool IsResultAccount(string code) => code.Length > 0 && code[0] is not ('1' or '2' or '3');

    private static readonly string[] MonthNames = ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio",
        "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"];

    // Detail accounts between two codes (inclusive, either end optional) from fromMonth to throughMonth.
    // Opening includes everything before the first month (FREDI journals and GL2000 imported amounts).
    // FREDI journal lines are listed one by one; GL2000 amounts only exist as monthly totals, so each
    // imported month appears as one line.
    public async Task<LedgerRangeReport> LedgerRange(int companyId, int year, int fromMonth, int throughMonth,
        string fromCode = "", string throughCode = "")
    {
        RequireCompany(companyId);
        fromCode = fromCode.Trim(); throughCode = throughCode.Trim();
        var start = PeriodStart(year, fromMonth);
        var end = PeriodStart(year, throughMonth).AddMonths(1);
        if (end <= start) throw new InvalidOperationException("El mes inicial debe ser anterior o igual al final.");
        if (fromCode.Length > 0 && throughCode.Length > 0 && string.CompareOrdinal(fromCode, throughCode) > 0)
            throw new InvalidOperationException("La cuenta inicial debe ser menor o igual a la final.");
        var snapshot = await ReadReportSnapshot(companyId, end);
        var openings = BuildBalances(snapshot, start).ToDictionary(b => b.Code);
        var parents = snapshot.Accounts.Select(a => a.ParentCode).ToHashSet();
        var linesByAccount = snapshot.Lines.Where(l => l.Date >= start).ToLookup(l => l.AccountId);
        var importedByAccount = snapshot.Imported.ToLookup(b => b.AccountId);
        var result = new List<AccountLedger>();
        foreach (var account in snapshot.Accounts.Where(a => !parents.Contains(a.Code)))
        {
            if ((fromCode.Length > 0 && string.CompareOrdinal(account.Code, fromCode) < 0) ||
                (throughCode.Length > 0 && string.CompareOrdinal(account.Code, throughCode) > 0)) continue;
            var movements = linesByAccount[account.Id]
                .OrderBy(l => l.Date).ThenBy(l => l.Type, StringComparer.Ordinal).ThenBy(l => l.Reference, StringComparer.Ordinal)
                .ThenBy(l => l.JournalId).ThenBy(l => l.LineId)
                .Select(l => new LedgerMovement(l.Date, l.Type, l.Reference, l.Concept, l.DebitCents, l.CreditCents)).ToList();
            var imported = importedByAccount[account.Id].ToList();
            var importedYear = imported.Where(b => b.Year <= year).Select(b => b.Year).DefaultIfEmpty(0).Max();
            if (importedYear == year)
                movements.InsertRange(0, imported.Where(b => b.Year == year && b.Month >= fromMonth && b.Month <= throughMonth && b.Month > 0)
                    .OrderBy(b => b.Month)
                    .Select(b => new LedgerMovement(new DateTime(year, b.Month, 1), "GL", "", $"Acumulado de {MonthNames[b.Month - 1]} (GL2000)",
                        b.DebitCents, b.CreditCents)));
            var opening = openings[account.Code].Opening;
            if (opening != 0 || movements.Count > 0) result.Add(new(account.Code, account.Description, opening, movements));
        }
        return new(snapshot.CompanyName, year, fromMonth, throughMonth, fromCode, throughCode, result);
    }

    public async Task<LedgerReport> Ledger(int companyId, int accountId, DateTime from,
        DateTime through, bool includeChildren = true)
    {
        RequireCompany(companyId);
        from = from.Date;
        through = through.Date;
        if (from.Year < 1900 || through.Year > 2100 || from > through)
            throw new InvalidOperationException("Indique un rango de fechas válido entre 1900 y 2100.");
        var snapshot = await ReadReportSnapshot(companyId, through.AddDays(1));
        var selected = snapshot.Accounts.FirstOrDefault(a => a.Id == accountId)
            ?? throw new InvalidOperationException("La cuenta no pertenece a esta contabilidad.");
        var ids = new HashSet<int> { selected.Id };
        if (includeChildren)
        {
            var codes = new HashSet<string> { selected.Code };
            bool changed;
            do
            {
                changed = false;
                foreach (var account in snapshot.Accounts)
                {
                    if (!codes.Contains(account.ParentCode) || !ids.Add(account.Id)) continue;
                    codes.Add(account.Code);
                    changed = true;
                }
            } while (changed);
        }
        var relevant = snapshot.Lines.Where(l => ids.Contains(l.AccountId)).ToList();
        var opening = relevant.Where(l => l.Date < from)
            .Aggregate(0L, (sum, l) => checked(sum + l.DebitCents - l.CreditCents));
        var running = opening;
        var byId = snapshot.Accounts.ToDictionary(a => a.Id);
        var rows = new List<LedgerRow>();
        foreach (var line in relevant.Where(l => l.Date >= from)
            .OrderBy(l => l.Date).ThenBy(l => l.Type, StringComparer.Ordinal)
            .ThenBy(l => l.Reference, StringComparer.Ordinal).ThenBy(l => l.JournalId).ThenBy(l => l.LineId))
        {
            running = checked(running + line.DebitCents - line.CreditCents);
            rows.Add(new(line.JournalId, line.LineId, line.Date, line.Type, line.Reference,
                byId[line.AccountId].Code, line.Concept, line.DebitCents, line.CreditCents, running));
        }
        return new(snapshot.CompanyName, selected.Code, selected.Description, from, through,
            includeChildren, opening, rows);
    }
}
