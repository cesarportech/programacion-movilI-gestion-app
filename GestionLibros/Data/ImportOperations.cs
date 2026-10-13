using GestionLibros.Data.Import;
using GestionLibros.Models;

namespace GestionLibros.Data;

public sealed record ImportResult(int Added, int Skipped, int BalanceAccounts = 0);

public sealed partial class AppDatabase
{
    // Adds the GL2000 accounts that the company does not have yet. Existing codes are kept untouched,
    // and the whole import is rolled back if any account breaks the catalog rules.
    // With a year, the GL accumulated amounts of that year replace any previously imported ones.
    public async Task<ImportResult> ImportAccounts(int companyId, IReadOnlyList<GlAccount> source, int? year = null, string sourceName = "GL2000")
    {
        RequireCompany(companyId);
        if (source.Count == 0) throw new InvalidOperationException("El catálogo seleccionado no tiene cuentas.");
        if (year is < 1900 or > 2100) throw new InvalidOperationException("El ejercicio del catálogo no es válido.");
        var incoming = source.Select(a => a with { Code = a.Code.Trim(), ParentCode = a.ParentCode.Trim(), Description = a.Description.Trim() }).ToList();
        foreach (var a in incoming)
            if (a.Code.Length == 0 || a.Code.Length > Account.CodeMaxLength || a.ParentCode.Length > Account.CodeMaxLength || a.Description.Length == 0 || a.Description.Length > 200)
                throw new InvalidOperationException($"La cuenta {a.Code} debe tener cuenta y superior de hasta 7 caracteres (incluido el punto) y una descripción válida.");
        var duplicate = incoming.GroupBy(a => a.Code).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null) throw new InvalidOperationException($"La cuenta {duplicate.Key} aparece repetida en el catálogo.");

        var added = 0; var skipped = 0; var balanceAccounts = 0;
        await db.RunInTransactionAsync(c =>
        {
            RequireCompany(companyId);
            RequireMutableCatalog(c, companyId);
            if (c.Find<Company>(companyId) == null) throw new InvalidOperationException("Contabilidad no disponible.");
            MigrationPolicy? policy = null;
            ImportBatch? batch = null;
            if (year != null)
            {
                policy = RequireMigration(c, companyId);
                ValidateMigrationState(c, companyId);
                if (year > policy.LegacyThrough.Year) throw new InvalidOperationException("El ejercicio importado supera el corte GL2000.");
                var throughMonth = year == policy.LegacyThrough.Year ? policy.LegacyThrough.Month : 12;
                if (incoming.Any(a => a.Balances == null)) throw new InvalidOperationException("Faltan acumulados en el archivo. Importe solo el catálogo, sin ejercicio; no se interpretará información ausente como cero.");
                foreach (var a in incoming.Where(a => a.Balances != null))
                {
                    var b = a.Balances!;
                    if (b.Opening == long.MinValue || b.Debits == null || b.Credits == null || b.Debits.Length != 12 || b.Credits.Length != 12 || b.Debits.Any(v => v < 0) || b.Credits.Any(v => v < 0)) throw new InvalidOperationException("Acumulados mensuales inválidos.");
                    if (b.Debits.Skip(throughMonth).Any(v => v != 0) || b.Credits.Skip(throughMonth).Any(v => v != 0)) throw new InvalidOperationException("El archivo contiene movimientos posteriores al corte. Seleccione un respaldo del período correcto; no se recortarán importes silenciosamente.");
                }
                var payload = System.Text.Json.JsonSerializer.Serialize(incoming.OrderBy(a => a.Code, StringComparer.Ordinal).Select(a => new { a.Code, a.ParentCode, a.Description, a.Balances }));
                batch = NewBatch(companyId, "Acumulados GL2000", sourceName, year.Value, 0, throughMonth, payload);
                var latest = c.Table<ImportBatch>().Where(b => b.CompanyId == companyId && b.Year == year && b.Kind == "Acumulados GL2000").OrderByDescending(b => b.ImportedUtc).FirstOrDefault();
                if (latest?.Fingerprint == batch.Fingerprint) { skipped = incoming.Count; balanceAccounts = incoming.Count(a => a.Balances is { IsZero: false }); return; }
                if (c.Table<DetailedPeriod>().Any(p => p.CompanyId == companyId && p.Year == year)) throw new InvalidOperationException("Este ejercicio ya tiene períodos sustituidos por detalle. No se reemplazarán sus acumulados con otro lote sin conciliación.");
                var previousIds = c.Table<ImportedBalance>().Where(b => b.CompanyId == companyId && b.Year == year).ToList().Select(b => b.AccountId).ToHashSet();
                if (c.Table<Account>().Where(a => a.CompanyId == companyId).ToList().Any(a => previousIds.Contains(a.Id) && !incoming.Any(i => i.Code == a.Code))) throw new InvalidOperationException("El archivo omite cuentas importadas anteriormente. No se reemplazará el ejercicio con un catálogo parcial.");
            }
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
            foreach (var previousBatch in c.Table<ImportedBalance>().Where(b => b.CompanyId == companyId && b.Year == year).ToList().Select(b => b.ImportBatchId).Distinct())
                c.Execute("UPDATE ImportBatch SET SupersededBy = ? WHERE Id = ?", batch!.Id, previousBatch);
            c.Execute("DELETE FROM ImportedBalance WHERE CompanyId = ? AND Year = ?", companyId, year.Value);
            var parentCodes = existing.Values.Select(a => a.ParentCode).ToHashSet();
            foreach (var a in incoming)
            {
                var accountId = existing[a.Code].Id;
                var b = a.Balances ?? new GlBalances(0, new long[12], new long[12]);
                var rows = new List<ImportedBalance> { new() { Month = 0, RollupOnly = b.IsZero && parentCodes.Contains(a.Code), DebitCents = Math.Max(0, b.Opening), CreditCents = Math.Max(0, -b.Opening) } };
                for (var m = 0; m < batch!.ThroughMonth; m++)
                    if (b.Debits[m] != 0 || b.Credits[m] != 0)
                        rows.Add(new() { Month = m + 1, DebitCents = b.Debits[m], CreditCents = b.Credits[m] });
                foreach (var row in rows) { row.CompanyId = companyId; row.AccountId = accountId; row.Year = year.Value; row.ImportBatchId = batch!.Id; c.Insert(row); }
                if (!b.IsZero) balanceAccounts++;
            }
            c.Insert(batch!);
        });
        return new(added, skipped, balanceAccounts);
    }
}


