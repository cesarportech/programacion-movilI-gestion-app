using GestionLibros.Models;

namespace GestionLibros.Data;

public sealed partial class AppDatabase
{
    public enum ConsultationSource { Reconciled, ImportedOnly, LocalOnly }

    public async Task<bool> ConsultationNeedsSource(int companyId)
    {
        RequireCompany(companyId);
        return await db.FindAsync<MigrationPolicy>(companyId) == null &&
            await db.Table<ImportedBalance>().Where(b => b.CompanyId == companyId).CountAsync() > 0;
    }
    // SQLite maps the joined columns into this read model; it is not a stored table.
    public sealed class PostedLine
    {
        public string ImportBatchId { get; set; } = "";
        public string SourceId { get; set; } = "";
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

    private record ReportSnapshot(string CompanyName, List<Account> Accounts, List<PostedLine> Lines, List<ImportedBalance> Imported, List<DetailedPeriod> Detailed);

    private async Task<ReportSnapshot> ReadReportSnapshot(int companyId, DateTime before, ConsultationSource source = ConsultationSource.Reconciled)
    {
        RequireCompany(companyId);
        ReportSnapshot? snapshot = null;
        // One transaction keeps the account tree, headers and movements consistent.
        await db.RunInTransactionAsync(connection =>
        {
            RequireCompany(companyId);
            if (!Enum.IsDefined(source)) throw new InvalidOperationException("Origen de consulta inválido.");
            if (source == ConsultationSource.Reconciled) ValidateMigrationState(connection, companyId);
            var company = connection.Find<Company>(companyId)
                ?? throw new InvalidOperationException("Contabilidad no disponible.");
            var accounts = connection.Table<Account>().Where(a => a.CompanyId == companyId)
                .OrderBy(a => a.Code).ToList();
            var lines = connection.Query<PostedLine>(
                "SELECT j.ImportBatchId, j.SourceId, j.Id AS JournalId, l.Id AS LineId, l.AccountId, j.Date, j.Type, j.Reference, " +
                "CASE WHEN l.Concept = '' THEN j.Concept ELSE l.Concept END AS Concept, " +
                "l.DebitCents, l.CreditCents FROM JournalLine l INNER JOIN Journal j ON j.Id = l.JournalId " +
                "INNER JOIN Account a ON a.Id = l.AccountId " +
                "WHERE j.CompanyId = ? AND a.CompanyId = ? AND j.Date < ?",
                companyId, companyId, before);
            var imported = connection.Table<ImportedBalance>().Where(b => b.CompanyId == companyId).ToList();
            var detailed = connection.Table<DetailedPeriod>().Where(p => p.CompanyId == companyId).ToList();
            // Unreconciled sources are independently readable, never silently added together.
            if (source == ConsultationSource.ImportedOnly) lines = lines.Where(l => !string.IsNullOrEmpty(l.ImportBatchId)).ToList();
            if (source == ConsultationSource.LocalOnly) { lines = lines.Where(l => string.IsNullOrEmpty(l.ImportBatchId)).ToList(); imported = []; detailed = []; }
            snapshot = new(company.Name, accounts, lines, imported, detailed);
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
        var calculator = new LedgerCalculator(snapshot);
        var parents = snapshot.Accounts.Select(a => a.ParentCode).ToHashSet();
        return snapshot.Accounts.Select(account =>
        {
            var amounts = calculator.Calculate(account, start, start.AddMonths(1).AddDays(-1), true);
            return new BalanceRow(account.Code, account.Description, account.Level, amounts.Opening,
                amounts.Debits, amounts.Credits, !parents.Contains(account.Code));
        }).ToList();
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
        return new(snapshot.CompanyName, year, month, maximumLevel, includeZeroAccounts, visible, totals, detail);
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
        var allRows = monthRows.Where(b => IsResultAccount(b.Code))
            .Select(b => new IncomeRow(b.Code, b.Description, b.Level, b.IsDetail, checked(b.Debits - b.Credits),
                checked(b.Closing - yearOpening[b.Code])))
            .Where(r => r.Month != 0 || r.YearToDate != 0).ToList();
        var visible = allRows.Where(r => r.Level <= maximumLevel).ToList();
        var detail = allRows.Where(r => r.IsDetail).ToList();
        return new(snapshot.CompanyName, year, month, maximumLevel, visible, detail);
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
        var calculator = new LedgerCalculator(snapshot);
        var parents = snapshot.Accounts.Select(a => a.ParentCode).ToHashSet();
        var result = new List<AccountLedger>();
        foreach (var account in snapshot.Accounts.Where(a => !parents.Contains(a.Code)))
        {
            if ((fromCode.Length > 0 && string.CompareOrdinal(account.Code, fromCode) < 0) ||
                (throughCode.Length > 0 && string.CompareOrdinal(account.Code, throughCode) > 0)) continue;
            var report = calculator.Calculate(account, start, end.AddDays(-1), true);
            var movements = report.Rows.Select(r => new LedgerMovement(r.Date, r.Type, r.Reference,
                r.Concept, r.Debits, r.Credits, r.IsImported, r.Origin)).ToList();
            if (report.Opening != 0 || movements.Count > 0)
                result.Add(new(account.Code, account.Description, report.Opening, movements, report.HasImportedBalances, report.OriginNote));
        }
        return new(snapshot.CompanyName, year, fromMonth, throughMonth, fromCode, throughCode, result);
    }

    public async Task<LedgerReport> Ledger(int companyId, int accountId, DateTime from,
        DateTime through, bool includeChildren = true, ConsultationSource source = ConsultationSource.Reconciled)
    {
        RequireCompany(companyId);
        from = from.Date;
        through = through.Date;
        if (from.Year < 1900 || through.Year > 2100 || from > through)
            throw new InvalidOperationException("Indique un rango de fechas válido entre 1900 y 2100.");
        var snapshot = await ReadReportSnapshot(companyId, through.AddDays(1), source);
        var selected = snapshot.Accounts.FirstOrDefault(a => a.Id == accountId)
            ?? throw new InvalidOperationException("La cuenta no pertenece a esta contabilidad.");
        return new LedgerCalculator(snapshot).Calculate(selected, from, through, includeChildren);
    }
}
