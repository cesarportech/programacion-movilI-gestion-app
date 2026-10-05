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

    private record ReportSnapshot(string CompanyName, List<Account> Accounts, List<PostedLine> Lines);

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
            snapshot = new(company.Name, accounts, lines);
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
        var byCode = snapshot.Accounts.ToDictionary(a => a.Code);
        foreach (var account in snapshot.Accounts.OrderByDescending(a => a.Level))
        {
            if (!byCode.TryGetValue(account.ParentCode, out var parent)) continue;
            for (var i = 0; i < 3; i++)
                totals[parent.Id][i] = checked(totals[parent.Id][i] + totals[account.Id][i]);
        }
        return snapshot.Accounts.Select(a => new BalanceRow(a.Code, a.Description, a.Level,
            totals[a.Id][0], totals[a.Id][1], totals[a.Id][2])).ToList();
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
        var parents = snapshot.Accounts.Select(a => a.ParentCode).ToHashSet();
        var detail = balances.Where(b => !parents.Contains(b.Code)).ToList();
        var totals = new TrialBalanceTotals(
            detail.Sum(b => Math.Max(0, b.Opening)), detail.Sum(b => Math.Max(0, -b.Opening)),
            detail.Sum(b => b.Debits), detail.Sum(b => b.Credits),
            detail.Sum(b => Math.Max(0, b.Closing)), detail.Sum(b => Math.Max(0, -b.Closing)));
        var visible = balances.Where(b => b.Level <= maximumLevel &&
            (includeZeroAccounts || b.Opening != 0 || b.Debits != 0 || b.Credits != 0 || b.Closing != 0)).ToList();
        return new(snapshot.CompanyName, year, month, maximumLevel, includeZeroAccounts, visible, totals);
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
