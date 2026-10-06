namespace GestionLibros.Models;

public record TrialBalanceTotals(long OpeningDebit, long OpeningCredit, long Debits,
    long Credits, long ClosingDebit, long ClosingCredit)
{
    public bool IsBalanced => OpeningDebit == OpeningCredit && Debits == Credits && ClosingDebit == ClosingCredit;
}

public record TrialBalanceReport(string CompanyName, int Year, int Month, int MaximumLevel,
    bool IncludeZeroAccounts, IReadOnlyList<BalanceRow> Rows, TrialBalanceTotals Totals);

public record LedgerRow(int JournalId, int LineId, DateTime Date, string Type, string Reference,
    string AccountCode, string Concept, long Debits, long Credits, long Balance);

// Estado de Resultados row: net movement (debit positive) of the month and of the year to date.
public record IncomeRow(string Code, string Description, int Level, bool IsDetail, long Month, long YearToDate);

public record IncomeStatementReport(string CompanyName, int Year, int MonthNumber, int MaximumLevel, IReadOnlyList<IncomeRow> Rows);

// "Reporte Auxiliares de Mayor": every detail account in a code range over a range of months.
public record LedgerMovement(DateTime Date, string Type, string Reference, string Concept, long Debits, long Credits);

public record AccountLedger(string Code, string Description, long Opening, IReadOnlyList<LedgerMovement> Movements)
{
    public long Debits => Movements.Aggregate(0L, (sum, m) => checked(sum + m.Debits));
    public long Credits => Movements.Aggregate(0L, (sum, m) => checked(sum + m.Credits));
    public long Closing => checked(Opening + Debits - Credits);
}

public record LedgerRangeReport(string CompanyName, int Year, int FromMonth, int ThroughMonth,
    string FromCode, string ThroughCode, IReadOnlyList<AccountLedger> Accounts);

public record LedgerReport(string CompanyName, string AccountCode, string AccountName,
    DateTime From, DateTime Through, bool IncludesChildren, long Opening,
    IReadOnlyList<LedgerRow> Rows)
{
    public long Debits => Rows.Aggregate(0L, (sum, row) => checked(sum + row.Debits));
    public long Credits => Rows.Aggregate(0L, (sum, row) => checked(sum + row.Credits));
    public long Closing => checked(Opening + Debits - Credits);
}
