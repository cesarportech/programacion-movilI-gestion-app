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
   var account = c.Find<Account>(id);
   if (account == null || account.CompanyId != companyId) throw new InvalidOperationException("Cuenta no disponible.");
   account.Description = description; account.ChangedBy = user.Username; account.ChangedAt = DateTime.Now; account.Status = "C"; c.Update(account);
  });
 }
 public async Task DeleteAccount(int companyId, int id)
 {
  RequireCompany(companyId);
  await db.RunInTransactionAsync(c => {
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
 public async Task SaveJournal(int companyId, Journal draft, IReadOnlyList<JournalLine> lines)
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
  await db.RunInTransactionAsync(c => {
   if (c.Find<Company>(companyId) == null) throw new InvalidOperationException("Contabilidad no disponible.");
   if (draft.Id != 0) {
    var existing = c.Find<Journal>(draft.Id);
    if (existing == null || existing.CompanyId != companyId) throw new InvalidOperationException("Póliza no disponible.");
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
  });
 }
 public async Task DeleteJournal(int companyId, int id)
 {
  RequireCompany(companyId);
  await db.RunInTransactionAsync(c => {
   var journal = c.Find<Journal>(id);
   if (journal == null || journal.CompanyId != companyId) throw new InvalidOperationException("Póliza no disponible.");
   c.Execute("DELETE FROM JournalLine WHERE JournalId = ?", id); c.Delete(journal);
  });
 }
 public async Task<List<BalanceRow>> Balances(int companyId, int year, int month)
 {
  RequireCompany(companyId);
  var start = PeriodStart(year, month);
  var snapshot = await ReadReportSnapshot(companyId, start.AddMonths(1));
  return BuildBalances(snapshot, start);
 }
}
