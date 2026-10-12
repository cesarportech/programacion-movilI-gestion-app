using GestionLibros.Models;

namespace GestionLibros.Data;

public sealed partial class AppDatabase
{
    // Shared read-only calculation over one database snapshot, used by all balances and ledgers.
    private sealed class LedgerCalculator
    {
        private readonly ReportSnapshot snapshot;
        private readonly Dictionary<int, Account> accounts;
        private readonly ILookup<string, Account> children;
        private readonly ILookup<int, PostedLine> lines;
        private readonly ILookup<int, ImportedBalance> imports;

        public LedgerCalculator(ReportSnapshot snapshot)
        {
            this.snapshot = snapshot;
            accounts = snapshot.Accounts.ToDictionary(a => a.Id);
            children = snapshot.Accounts.Where(a => a.ParentCode.Length > 0).ToLookup(a => a.ParentCode);
            lines = snapshot.Lines.ToLookup(l => l.AccountId);
            imports = snapshot.Imported.ToLookup(b => b.AccountId);
        }

        public LedgerReport Calculate(Account selected, DateTime from, DateTime through, bool includeChildren)
        {
            var ids = new HashSet<int>();
            void Visit(Account account)
            {
                if (!ids.Add(account.Id) || !includeChildren) return;
                foreach (var child in children[account.Code]) Visit(child);
            }
            Visit(selected);
            // A later imported annual opening may replace the previous year's history. A single
            // running ledger cannot cross that boundary without inventing a reconciliation entry.
            if (from.Year != through.Year && ids.Any(id => imports[id].Any(b => b.Year <= through.Year)))
                throw new InvalidOperationException("Hay saldos GL2000 importados. Consulte el Mayor por ejercicio para respetar el saldo inicial de cada año.");

            var imported = new List<ImportedBalance>();
            void Collect(Account account)
            {
                var available = imports[account.Id].Where(b => b.Year <= from.Year).ToList();
                if (available.Count > 0)
                {
                    var latest = available.Max(b => b.Year);
                    imported.AddRange(available.Where(b => b.Year == latest));
                    return; // A parent's own GL totals take precedence over descendants' GL totals.
                }
                if (includeChildren) foreach (var child in children[account.Code]) Collect(child);
            }
            Collect(selected);
            var movements = new List<LedgerRow>();
            long opening = 0;
            foreach (var id in ids)
                foreach (var line in lines[id])
                {
                    if (line.Date < from) opening = checked(opening + line.DebitCents - line.CreditCents);
                    else if (line.Date <= through)
                        movements.Add(new(line.JournalId, line.LineId, line.Date, line.Type, line.Reference,
                            accounts[id].Code, line.Concept, line.DebitCents, line.CreditCents, 0));
                }
            foreach (var balance in imported)
            {
                if (balance.Year < from.Year || balance.Month == 0)
                {
                    opening = checked(opening + balance.DebitCents - balance.CreditCents);
                    continue;
                }
                var monthStart = new DateTime(balance.Year, balance.Month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                if (monthEnd < from)
                    opening = checked(opening + balance.DebitCents - balance.CreditCents);
                else if (monthStart <= through && (balance.DebitCents != 0 || balance.CreditCents != 0))
                {
                    if (from > monthStart || through < monthEnd)
                        throw new InvalidOperationException($"GL2000 solo tiene el acumulado de {MonthNames[balance.Month - 1]} de {balance.Year}, sin detalle diario. " +
                            $"Consulte ese mes completo ({monthStart:d/M/yyyy} al {monthEnd:d/M/yyyy}); no es posible calcular un saldo exacto con fechas parciales.");
                    // The date is an ordering key, not a transaction date: views/exports show MM/yyyy.
                    movements.Add(new(0, 0, monthEnd, "GL", "", accounts[balance.AccountId].Code,
                        $"Acumulado de {MonthNames[balance.Month - 1]} {balance.Year} (GL2000; sin detalle diario)",
                        balance.DebitCents, balance.CreditCents, 0, true));
                }
            }
            long running = opening;
            var ordered = movements.OrderBy(m => m.Date).ThenBy(m => m.IsImported)
                .ThenBy(m => m.Type, StringComparer.Ordinal).ThenBy(m => m.Reference, StringComparer.Ordinal)
                .ThenBy(m => m.JournalId).ThenBy(m => m.LineId).ThenBy(m => m.AccountCode, StringComparer.Ordinal)
                .Select(m => { running = checked(running + m.Debits - m.Credits); return m with { Balance = running }; }).ToList();
            return new(snapshot.CompanyName, selected.Code, selected.Description, from, through, includeChildren,
                opening, ordered, imported.Count > 0);
        }
    }
}
