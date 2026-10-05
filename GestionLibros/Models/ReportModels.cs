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

public record LedgerReport(string CompanyName, string AccountCode, string AccountName,
    DateTime From, DateTime Through, bool IncludesChildren, long Opening,
    IReadOnlyList<LedgerRow> Rows)
{
    public long Debits => Rows.Aggregate(0L, (sum, row) => checked(sum + row.Debits));
    public long Credits => Rows.Aggregate(0L, (sum, row) => checked(sum + row.Credits));
    public long Closing => checked(Opening + Debits - Credits);
}
