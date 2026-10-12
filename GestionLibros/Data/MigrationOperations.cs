using GestionLibros.Models;
using SQLite;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
namespace GestionLibros.Data;
public sealed partial class AppDatabase
{
 private static string MigrationHash(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
 private ImportBatch NewBatch(int company, string kind, string source, int year, int first, int last, string payload) => new()
 {
  Id = Guid.NewGuid().ToString("N"), CompanyId = company, Kind = kind, Source = source,
  Year = year, FromMonth = first, ThroughMonth = last, Payload = payload, Fingerprint = MigrationHash(payload),
  ImportedUtc = DateTime.UtcNow, ImportedBy = RequireSession().Username
 };
 public async Task<MigrationPolicy?> MigrationCutoff(int companyId)
 { RequireCompany(companyId); return await db.FindAsync<MigrationPolicy>(companyId); }
 public async Task<List<ImportBatch>> MigrationBatches(int companyId)
 { RequireCompany(companyId); return await db.Table<ImportBatch>().Where(b => b.CompanyId == companyId).OrderByDescending(b => b.ImportedUtc).ToListAsync(); }
 public async Task SetMigrationCutoff(int companyId, DateTime through)
 {
  RequireCompany(companyId); through = through.Date;
  if (through.Year is < 1900 or > 2100 || through.Day != DateTime.DaysInMonth(through.Year, through.Month))
   throw new InvalidOperationException("El corte GL2000 debe ser el último día de un mes (1900–2100).");
  await db.RunInTransactionAsync(c =>
  {
   RequireCompany(companyId);
   var current = c.Find<MigrationPolicy>(companyId);
   if (current != null && current.LegacyThrough == through) return;
   if (current != null && c.Table<ImportBatch>().Any(b => b.CompanyId == companyId))
    throw new InvalidOperationException("El corte ya tiene lotes asociados. No se puede mover sin reconciliar la migración.");
   var overlap = c.Table<Journal>().Where(j => j.CompanyId == companyId && j.Date <= through).ToList().Where(j => string.IsNullOrEmpty(j.ImportBatchId)).ToList();
   if (overlap.Count > 0) throw new InvalidOperationException($"Hay {overlap.Count} pólizas FREDI dentro del período GL2000 (primera: {overlap[0].Type}-{overlap[0].Reference}, {overlap[0].Date:dd/MM/yyyy}). Revise y resuelva el solapamiento antes de fijar el corte; no se ocultará ni borrará ninguna póliza.");
   var imported = c.Table<ImportedBalance>().Where(b => b.CompanyId == companyId).ToList();
   if (imported.Any(b => b.Year > through.Year || b.Year == through.Year && b.Month > through.Month && (b.DebitCents != 0 || b.CreditCents != 0)))
    throw new InvalidOperationException("Existen acumulados posteriores al corte propuesto. Revise el ejercicio y período.");
   c.InsertOrReplace(new MigrationPolicy { CompanyId = companyId, LegacyThrough = through, SetBy = RequireSession().Username, SetAtUtc = DateTime.UtcNow });
   foreach (var group in imported.Where(b => string.IsNullOrEmpty(b.ImportBatchId)).GroupBy(b => b.Year))
   {
    var batch = NewBatch(companyId, "Acumulados previos", "GL2000: archivo de origen no registrado en la versión anterior", group.Key, 0, group.Max(b => b.Month), JsonSerializer.Serialize(group.ToList()));
    c.Insert(batch);
    foreach (var balance in group) { balance.ImportBatchId = batch.Id; c.Update(balance); }
   }
  });
 }
 private static MigrationPolicy RequireMigration(SQLiteConnection c, int companyId) => c.Find<MigrationPolicy>(companyId)
  ?? throw new InvalidOperationException("Defina el corte en Utilerías → Corte y lotes de migración antes de importar saldos.");
 private static void ValidateMigrationState(SQLiteConnection c, int companyId)
 {
  var policy = c.Find<MigrationPolicy>(companyId);
  var imported = c.Table<ImportedBalance>().Any(b => b.CompanyId == companyId);
  if (imported && policy == null) throw new InvalidOperationException("Los saldos importados todavía no tienen corte de migración. Configure y reconcilie el corte antes de consultar saldos o guardar pólizas.");
  if (policy != null && c.Table<Journal>().Where(j => j.CompanyId == companyId && j.Date <= policy.LegacyThrough).ToList().Any(j => string.IsNullOrEmpty(j.ImportBatchId)))
   throw new InvalidOperationException("Hay partidas FREDI solapadas con el período GL2000. Resuelva el solapamiento antes de calcular saldos.");
 }
 // A complete month replaces its aggregate only after reconciling gross amounts at every account.
 public async Task<string> ImportHistoricalDetails(int companyId, MigrationDetailFile file, string source)
 {
  RequireCompany(companyId);
  var start = PeriodStart(file.Year, file.Month); var end = start.AddMonths(1).AddDays(-1);
  var journals = file.Journals ?? throw new InvalidOperationException("Falta la lista de pólizas.");
  if (journals.Count == 0) throw new InvalidOperationException("El lote no contiene pólizas.");
  foreach (var j in journals)
  {
   if (j == null || string.IsNullOrWhiteSpace(j.SourceId) || string.IsNullOrWhiteSpace(j.Type) || string.IsNullOrWhiteSpace(j.Reference) || string.IsNullOrWhiteSpace(j.Concept) || j.SourceId.Length > 120 || j.Type.Length > 20 || j.Reference.Length > 60 || j.Concept.Length > 300 || j.Date.Date < start || j.Date.Date > end || j.Lines == null || j.Lines.Count < 2)
    throw new InvalidOperationException("Cada póliza requiere identificador de origen, tipo, referencia, concepto y al menos dos movimientos dentro del mes indicado.");
   if (j.Lines.Any(l => l == null || string.IsNullOrWhiteSpace(l.AccountCode) || l.Concept == null || l.Concept.Length > 300 || l.DebitCents < 0 || l.CreditCents < 0 || (l.DebitCents == 0) == (l.CreditCents == 0) || l.DebitCents > 999999999999L || l.CreditCents > 999999999999L) || j.Lines.Sum(l => l.DebitCents) != j.Lines.Sum(l => l.CreditCents))
    throw new InvalidOperationException($"La póliza {j.SourceId} no cuadra o tiene importes inválidos. Los importes se expresan en centavos.");
  }
  if (journals.GroupBy(j => j.SourceId.Trim()).Any(g => g.Count() > 1) || journals.GroupBy(j => (j.Type.Trim(), j.Reference.Trim())).Any(g => g.Count() > 1))
   throw new InvalidOperationException("El lote contiene identificadores o referencias repetidas.");
  var canonical = journals.OrderBy(j => j.SourceId, StringComparer.Ordinal).Select(j => j with { SourceId = j.SourceId.Trim(), Date = j.Date.Date, Type = j.Type.Trim(), Reference = j.Reference.Trim(), Lines = j.Lines.OrderBy(l => l.AccountCode, StringComparer.Ordinal).ThenBy(l => l.Concept, StringComparer.Ordinal).ThenBy(l => l.DebitCents).ThenBy(l => l.CreditCents).ToList() }).ToList();
  var batch = NewBatch(companyId, "Detalle GL2000", source, file.Year, file.Month, file.Month, JsonSerializer.Serialize(canonical));
  await db.RunInTransactionAsync(c =>
  {
   RequireCompany(companyId); ValidateMigrationState(c, companyId);
   var policy = RequireMigration(c, companyId);
   if (end > policy.LegacyThrough) throw new InvalidOperationException("El detalle supera el corte GL2000.");
   var key = $"{companyId}:{file.Year}:{file.Month}";
   var previous = c.Find<DetailedPeriod>(key);
   if (previous != null)
   {
    if (c.Find<ImportBatch>(previous.BatchId)?.Fingerprint == batch.Fingerprint) { batch.Id = previous.BatchId; return; }
    throw new InvalidOperationException("Este mes ya tiene detalle importado. Un lote diferente requiere conciliación; no se añadirá de nuevo.");
   }
   var balances = c.Table<ImportedBalance>().Where(b => b.CompanyId == companyId && b.Year == file.Year).ToList();
   if (balances.Count == 0) throw new InvalidOperationException("Importe primero los acumulados del ejercicio para conciliar el detalle.");
   var accounts = c.Table<Account>().Where(a => a.CompanyId == companyId).ToList().ToDictionary(a => a.Code);
   var parents = accounts.Values.Select(a => a.ParentCode).ToHashSet();
   var sums = new Dictionary<int, (long Debit, long Credit)>();
   foreach (var line in canonical.SelectMany(j => j.Lines))
   {
    if (!accounts.TryGetValue(line.AccountCode, out var account) || parents.Contains(account.Code)) throw new InvalidOperationException($"{line.AccountCode} no es una cuenta de detalle válida.");
    while (true)
    {
     var old = sums.GetValueOrDefault(account.Id); sums[account.Id] = (checked(old.Debit + line.DebitCents), checked(old.Credit + line.CreditCents));
     if (account.ParentCode.Length == 0) break;
     account = accounts[account.ParentCode];
    }
   }
   foreach (var account in accounts.Values)
   {
    // Parents with no own aggregate roll up their children; own GL parent totals must also reconcile.
    if (parents.Contains(account.Code) && !balances.Any(b => b.AccountId == account.Id && !b.RollupOnly)) continue;
    var expected = balances.Where(b => b.AccountId == account.Id && b.Month == file.Month);
    var actual = sums.GetValueOrDefault(account.Id);
    if (expected.Sum(b => b.DebitCents) != actual.Debit || expected.Sum(b => b.CreditCents) != actual.Credit)
     throw new InvalidOperationException($"El detalle no coincide con cargos/créditos del acumulado de {account.Code}. No se importó ninguna póliza.");
   }
   foreach (var j in canonical)
   {
    if (c.Table<Journal>().Any(old => old.CompanyId == companyId && ((old.Year == file.Year && old.Type == j.Type && old.Reference == j.Reference) || old.SourceId == j.SourceId)))
     throw new InvalidOperationException($"La póliza {j.SourceId} ya existe. No se duplicará.");
    var header = new Journal { CompanyId = companyId, Year = file.Year, Month = file.Month, Date = j.Date, Type = j.Type, Reference = j.Reference, Concept = j.Concept,
     ImportBatchId = batch.Id, SourceId = j.SourceId, ChangedBy = RequireSession().Username, ChangedAt = DateTime.Now };
    c.Insert(header);
    foreach (var l in j.Lines) c.Insert(new JournalLine { JournalId = header.Id, AccountId = accounts[l.AccountCode].Id, Concept = l.Concept, DebitCents = l.DebitCents, CreditCents = l.CreditCents });
   }
   batch.ReplacesBatchIds = string.Join(", ", balances.Select(b => b.ImportBatchId).Distinct());
   c.Insert(batch);
   c.Insert(new DetailedPeriod { Key = key, CompanyId = companyId, Year = file.Year, Month = file.Month, BatchId = batch.Id });
  });
  return batch.Id;
 }
}
