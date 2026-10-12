using GestionLibros.Models;
using System.Text.Json;
namespace GestionLibros.Data;
public sealed partial class AppDatabase
{
 public List<JournalDraft> JournalDrafts(int companyId, int journalId)
 {
  RequireCompany(companyId);
  var user = ValidateSession(false);
  var c = db.GetConnection();
  using (c.Lock()) return c.Table<JournalDraft>().Where(d => d.UserId == user.UserId && d.CompanyId == companyId && d.JournalId == journalId).OrderByDescending(d => d.UpdatedUtc).ToList();
 }
 // Synchronous durable snapshot on each edit; never posts or validates an unfinished journal.
 public void StoreJournalDraft(string id, int companyId, JournalDraftContent content)
 {
  var user = ValidateSession(false);
  if (user.IsMaster || user.CompanyId != companyId) throw new InvalidOperationException("No tiene acceso al borrador.");
  var c = db.GetConnection();
  using (c.Lock())
  {
   var existing = c.Find<JournalDraft>(id);
   if (existing != null && (existing.UserId != user.UserId || existing.CompanyId != companyId)) throw new InvalidOperationException("Borrador no disponible.");
   c.InsertOrReplace(new JournalDraft { Id = id, UserId = user.UserId, CompanyId = companyId, JournalId = content.Header.Id,
    UpdatedUtc = DateTime.UtcNow, Payload = JsonSerializer.Serialize(content) });
  }
 }
 public void DeleteJournalDraft(string id, int companyId)
 {
  RequireCompany(companyId);
  var user = RequireSession();
  var c = db.GetConnection();
  using (c.Lock()) c.Execute("DELETE FROM JournalDraft WHERE Id = ? AND UserId = ? AND CompanyId = ?", id, user.UserId, companyId);
 }
}
