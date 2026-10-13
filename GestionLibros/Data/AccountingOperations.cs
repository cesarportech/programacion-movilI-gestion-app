using GestionLibros.Models;
namespace GestionLibros.Data;
public sealed partial class AppDatabase
{
 public async Task UpdateAccount(int companyId, int id, string description)
 {
  RequireCompany(companyId);
  var user = RequireSession();
  description = description.Trim();
  if (description.Length == 0 || description.Length > 200) throw new InvalidOperationException("Escriba una descripción de hasta 200 caracteres.");
  await db.RunInTransactionAsync(c => {
  RequireSession();
   RequireMutableCatalog(c, companyId);
   var account = c.Find<Account>(id);
   if (account == null || account.CompanyId != companyId) throw new InvalidOperationException("Cuenta no disponible.");
   account.Description = description; account.ChangedBy = user.Username; account.ChangedAt = DateTime.Now; account.Status = "C"; c.Update(account);
  });
 }
 public async Task DeleteAccount(int companyId, int id)
 {
  RequireCompany(companyId);
  await db.RunInTransactionAsync(c => {
  RequireSession();
   RequireMutableCatalog(c, companyId);
   var account = c.Find<Account>(id);
   if (account == null || account.CompanyId != companyId) throw new InvalidOperationException("Cuenta no disponible.");
   if (c.Table<Account>().Any(a => a.CompanyId == companyId && a.ParentCode == account.Code) || c.Table<JournalLine>().Any(l => l.AccountId == id) || c.Table<ImportedBalance>().Any(b => b.AccountId == id)) throw new InvalidOperationException("No puede borrar una cuenta con subcuentas, movimientos o saldos importados.");
   c.Delete(account);
  });
 }
 public Task<List<Journal>> Journals(int companyId, int year, int month)
 {
  RequireCompany(companyId);
  return db.Table<Journal>().Where(j => j.CompanyId == companyId && j.Year == year && j.Month == month).OrderBy(j => j.Date).ThenBy(j => j.Reference).ToListAsync();
 }
 // Whole fiscal year in one query (tabs Numero/Mes and the type lookup used to run 12).
 public Task<List<Journal>> JournalsOfYear(int companyId, int year)
 {
  RequireCompany(companyId);
  return db.Table<Journal>().Where(j => j.CompanyId == companyId && j.Year == year).OrderBy(j => j.Date).ThenBy(j => j.Reference).ToListAsync();
 }
 public async Task<List<JournalLine>> Lines(int companyId, int journalId)
 {
  RequireCompany(companyId);
  var journal = await db.FindAsync<Journal>(journalId);
  if (journal == null || journal.CompanyId != companyId) throw new InvalidOperationException("Póliza no disponible.");
  return await db.Table<JournalLine>().Where(l => l.JournalId == journalId).ToListAsync();
 }
 // Resolve by persisted IDs, never by a displayed reference or row position.
 public async Task<Journal> JournalForMovement(int companyId, int journalId, int lineId)
 {
  RequireCompany(companyId);
  if (journalId <= 0 || lineId <= 0) throw new InvalidOperationException("Este importe es un acumulado sin póliza individual.");
  Journal? result = null;
  await db.RunInTransactionAsync(c =>
  {
   RequireCompany(companyId);
   var header = c.Find<Journal>(journalId);
   var line = c.Find<JournalLine>(lineId);
   if (header == null || header.CompanyId != companyId || line == null || line.JournalId != journalId || c.Find<Account>(line.AccountId)?.CompanyId != companyId)
    throw new InvalidOperationException("El movimiento cambió o ya no está disponible. Vuelva a consultar la cuenta.");
   result = header;
  });
  return result!;
 }
 public static void ValidateCaptureReference(string reference, DateTime date)
 {
  var prefix = $"{date.Year % 10}{date.Month:00}";
  if (reference.Length != 7 || reference.Any(c => c < '0' || c > '9') || !reference.StartsWith(prefix, StringComparison.Ordinal))
   throw new InvalidOperationException($"La referencia debe tener 7 dígitos: {prefix} (año y mes) y 4 dígitos elegidos por el contador. Ejemplo: {prefix}0001.");
 }
 public async Task<string> NextJournalReference(int companyId, string type, DateTime date)
 {
  RequireCompany(companyId);
  if (date.Year < 1900 || date.Year > 2100 || string.IsNullOrWhiteSpace(type)) throw new InvalidOperationException("Indique fecha y tipo de póliza.");
  var prefix = $"{date.Year % 10}{date.Month:00}";
  var journals = await Journals(companyId, date.Year, date.Month);
  var matching = journals.Where(j => j.Type == type.Trim() && j.Reference.Length == 7 && j.Reference.All(c => c >= '0' && c <= '9') && j.Reference.StartsWith(prefix, StringComparison.Ordinal)).OrderByDescending(j => j.Id).ToList();
  var next = matching.Count == 0 ? 1 : int.Parse(matching[0].Reference[3..], System.Globalization.CultureInfo.InvariantCulture) + 1;
  var used = matching.Select(j => j.Reference).ToHashSet();
  while (next <= 9999 && used.Contains(prefix + next.ToString("0000"))) next++;
  if (next > 9999) throw new InvalidOperationException("Se agotó el consecutivo de cuatro dígitos de esta póliza y mes. No se generará una referencia de más de 7 dígitos.");
  return prefix + next.ToString("0000");
 }
 public async Task<Journal?> PreviousJournalCapture(int companyId)
 {
  RequireCompany(companyId); var username = RequireSession().Username;
  return (await db.Table<Journal>().Where(j => j.CompanyId == companyId && j.ChangedBy == username).ToListAsync())
   .Where(j => string.IsNullOrEmpty(j.ImportBatchId)).OrderByDescending(j => j.ChangedAt).ThenByDescending(j => j.Id).FirstOrDefault();
 }
 // New interactive captures follow the numeric scheme; legacy imports retain their original references.
 public Task SaveCapturedJournal(int companyId, Journal draft, IReadOnlyList<JournalLine> lines, string? draftId = null, long? draftRevision = null)
 {
  RequireCompany(companyId); ValidateCaptureReference(draft.Reference.Trim(), draft.Date);
  return SaveJournal(companyId, draft, lines, draftId, draftRevision);
 }
 public async Task SaveJournal(int companyId, Journal draft, IReadOnlyList<JournalLine> lines, string? draftId = null, long? draftRevision = null)
 {
  RequireCompany(companyId);
  var user = RequireSession();
  if (draft.Date.Year < 1900 || draft.Date.Year > 2100) throw new InvalidOperationException("Fecha fuera del rango de pruebas (1900–2100).");
  if (string.IsNullOrWhiteSpace(draft.Type) || string.IsNullOrWhiteSpace(draft.Reference) || string.IsNullOrWhiteSpace(draft.Concept)) throw new InvalidOperationException("Complete tipo, referencia y concepto.");
  if (draft.Type.Length > 20 || draft.Reference.Length > 60 || draft.Concept.Length > 300) throw new InvalidOperationException("Tipo, referencia o concepto demasiado largos.");
  if (lines.Count < 2) throw new InvalidOperationException("Agregue al menos dos movimientos.");
  long debit = 0, credit = 0;
  foreach (var line in lines) {
   if (line.DebitCents < 0 || line.CreditCents < 0 || (line.DebitCents == 0) == (line.CreditCents == 0)) throw new InvalidOperationException("Cada movimiento debe tener cargo o crédito positivo, nunca ambos.");
   if (line.DebitCents > 999999999999L || line.CreditCents > 999999999999L) throw new InvalidOperationException("Importe fuera del límite de pruebas.");
   debit = checked(debit + line.DebitCents); credit = checked(credit + line.CreditCents);
  }
  if (debit != credit) throw new InvalidOperationException("La póliza no cuadra: los cargos y créditos deben ser iguales.");
  Journal? committed = null;
  await db.RunInTransactionAsync(c => {
  RequireSession();
   ValidateMigrationState(c, companyId);
   var cut = c.Find<MigrationPolicy>(companyId);
   if (cut != null && draft.Date.Date <= cut.LegacyThrough) throw new InvalidOperationException($"FREDI comienza el {cut.LegacyThrough.AddDays(1):dd/MM/yyyy}. Las fechas anteriores corresponden a migración, no a captura local.");
   if (c.Find<Company>(companyId) == null) throw new InvalidOperationException("Contabilidad no disponible.");
   if (draftId != null) {
    var pending = c.Find<JournalDraft>(draftId);
    if (pending != null && draftRevision != null && pending.Revision != draftRevision) throw new InvalidOperationException("El borrador cambió antes de aceptar. Revise la versión actual.");
    if (pending == null || pending.UserId != user.UserId || pending.CompanyId != companyId || pending.JournalId != draft.Id) throw new InvalidOperationException("Borrador no disponible.");
   }
   if (draft.Id != 0) {
    var existing = c.Find<Journal>(draft.Id);
    if (existing == null || existing.CompanyId != companyId) throw new InvalidOperationException("Póliza no disponible.");
    if (!string.IsNullOrEmpty(existing.ImportBatchId)) throw new InvalidOperationException("El detalle histórico pertenece a un lote y no se puede editar como póliza local.");
    if (draftId != null && existing.ChangedAt != draft.ChangedAt) throw new InvalidOperationException("La póliza cambió desde que se inició el borrador. Revise la versión actual antes de aceptar.");
   }
   var accounts = c.Table<Account>().Where(a => a.CompanyId == companyId).ToList();
   foreach (var line in lines) {
    var account = accounts.FirstOrDefault(a => a.Id == line.AccountId);
    if (account == null) throw new InvalidOperationException("Todas las cuentas deben pertenecer a la contabilidad activa.");
    if (accounts.Any(a => a.ParentCode == account.Code)) throw new InvalidOperationException("Capture movimientos en cuentas de detalle, sin subcuentas.");
   }
   var type = draft.Type.Trim(); var reference = draft.Reference.Trim(); var year = draft.Date.Year;
   if (c.Table<Journal>().Any(j => j.CompanyId == companyId && j.Year == year && j.Type == type && j.Reference == reference && j.Id != draft.Id)) throw new InvalidOperationException("Ya existe una póliza con ese tipo y referencia en el ejercicio.");
   var record = new Journal { Id = draft.Id, CompanyId = companyId, Date = draft.Date.Date, Year = year, Month = draft.Date.Month, Type = type, Reference = reference, Concept = draft.Concept.Trim(), ChangedBy = user.Username, ChangedAt = DateTime.Now };
   if (record.Id == 0) c.Insert(record); else { c.Update(record); c.Execute("DELETE FROM JournalLine WHERE JournalId = ?", record.Id); }
   foreach (var line in lines) c.Insert(new JournalLine { JournalId = record.Id, AccountId = line.AccountId, Concept = line.Concept.Trim(), DebitCents = line.DebitCents, CreditCents = line.CreditCents });
   if (draftId != null) c.Delete<JournalDraft>(draftId);
   committed = record;
  });
  draft.Id = committed!.Id; draft.ChangedAt = committed.ChangedAt;
 }
 public async Task DeleteJournal(int companyId, int id)
 {
  RequireCompany(companyId);
  await db.RunInTransactionAsync(c => {
  RequireSession();
   var journal = c.Find<Journal>(id);
   if (journal == null || journal.CompanyId != companyId) throw new InvalidOperationException("Póliza no disponible.");
   if (!string.IsNullOrEmpty(journal.ImportBatchId)) throw new InvalidOperationException("El detalle histórico pertenece a un lote y no se puede borrar individualmente.");
   c.Execute("DELETE FROM JournalLine WHERE JournalId = ?", id); c.Delete(journal);
  });
 }
 public async Task<List<BalanceRow>> Balances(int companyId, int year, int month, ConsultationSource source = ConsultationSource.Reconciled)
 {
  RequireCompany(companyId);
  var start = PeriodStart(year, month);
  var snapshot = await ReadReportSnapshot(companyId, start.AddMonths(1), source);
  return BuildBalances(snapshot, start);
 }
}


