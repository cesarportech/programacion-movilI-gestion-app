using GestionLibros.Data.Import;
using GestionLibros.Models;

namespace GestionLibros.Data;

public sealed record ImportResult(int Added, int Skipped, int BalanceAccounts = 0);

public sealed partial class AppDatabase
{
    // Adds the GL2000 accounts that the company does not have yet. Existing codes are kept untouched,
    // and the whole import is rolled back if any account breaks the catalog rules.
    // With a year, the GL accumulated amounts of that year replace any previously imported ones.
    public async Task<ImportResult> ImportAccounts(int companyId, IReadOnlyList<GlAccount> source, int? year = null)
    {
        RequireCompany(companyId);
        if (source.Count == 0) throw new InvalidOperationException("El catálogo seleccionado no tiene cuentas.");
        if (year is < 1900 or > 2100) throw new InvalidOperationException("El ejercicio del catálogo no es válido.");
        var incoming = source.Select(a => a with { Code = a.Code.Trim(), ParentCode = a.ParentCode.Trim(), Description = a.Description.Trim() }).ToList();
        foreach (var a in incoming)
            if (a.Code.Length == 0 || a.Code.Length > 40 || a.Description.Length == 0 || a.Description.Length > 200)
                throw new InvalidOperationException($"La cuenta {a.Code} no tiene un código o descripción válidos.");
        var duplicate = incoming.GroupBy(a => a.Code).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) throw new InvalidOperationException($"La cuenta {duplicate.Key} aparece repetida en el catálogo.");

        var added = 0; var skipped = 0; var balanceAccounts = 0;
        await db.RunInTransactionAsync(c =>
        {
            if (c.Find<Company>(companyId) == null) throw new InvalidOperationException("Contabilidad no disponible.");
            var existing = c.Table<Account>().Where(a => a.CompanyId == companyId).ToList().ToDictionary(a => a.Code);
            var posted = c.Query<JournalLine>("SELECT l.* FROM JournalLine l INNER JOIN Account a ON a.Id = l.AccountId WHERE a.CompanyId = ?", companyId)
                .Select(l => l.AccountId).ToHashSet();
            var pending = incoming.Where(a => !existing.ContainsKey(a.Code)).ToList();
            skipped = incoming.Count - pending.Count;
            // Insert parents before children; each pass adds the accounts whose parent already exists.
            while (pending.Count > 0)
            {
                var ready = pending.Where(a => a.ParentCode.Length == 0 || existing.ContainsKey(a.ParentCode)).ToList();
                if (ready.Count == 0)
                    throw new InvalidOperationException($"La cuenta {pending[0].Code} depende de {pending[0].ParentCode}, que no existe en el catálogo.");
                foreach (var a in ready)
                {
                    var parent = a.ParentCode.Length == 0 ? null : existing[a.ParentCode];
                    if (parent != null && posted.Contains(parent.Id))
                        throw new InvalidOperationException($"No se puede agregar {a.Code}: la cuenta superior {parent.Code} ya tiene movimientos.");
                    var level = parent == null ? 1 : parent.Level + 1;
                    if (level > 9) throw new InvalidOperationException($"La cuenta {a.Code} supera los 9 niveles permitidos.");
                    var account = new Account { CompanyId = companyId, Code = a.Code, ParentCode = a.ParentCode, Level = level,
                        Description = a.Description, ChangedAt = a.ChangedAt ?? default,
                        ChangedBy = a.ChangedBy, Status = a.Status };
                    c.Insert(account);
                    existing[a.Code] = account;
                    added++;
                }
                pending = pending.Except(ready).ToList();
            }
            if (year == null) return;
            // Every account keeps its own GL totals, parents included: the GL does not always keep a
            // parent equal to the sum of its children, and its screens show the parent's stored value.
            c.Execute("DELETE FROM ImportedBalance WHERE CompanyId = ? AND Year = ?", companyId, year.Value);
            foreach (var a in incoming.Where(a => a.Balances is { IsZero: false }))
            {
                var accountId = existing[a.Code].Id;
                var b = a.Balances!;
                var rows = new List<ImportedBalance> { new() { Month = 0, DebitCents = Math.Max(0, b.Opening), CreditCents = Math.Max(0, -b.Opening) } };
                for (var m = 0; m < 12; m++)
                    if (b.Debits[m] != 0 || b.Credits[m] != 0)
                        rows.Add(new() { Month = m + 1, DebitCents = b.Debits[m], CreditCents = b.Credits[m] });
                foreach (var row in rows) { row.CompanyId = companyId; row.AccountId = accountId; row.Year = year.Value; c.Insert(row); }
                balanceAccounts++;
            }
        });
        return new(added, skipped, balanceAccounts);
    }
}
