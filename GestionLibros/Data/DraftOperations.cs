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
 public long StoreJournalDraft(string id, int companyId, JournalDraftContent content, long? expectedRevision = null)
 {
  var user = ValidateSession(false);
  if (user.IsMaster || user.CompanyId != companyId) throw new InvalidOperationException("No tiene acceso al borrador.");
  var c = db.GetConnection();
  using (c.Lock())
  {
   long revision = 0;
   c.RunInTransaction(() =>
   {
   ValidateSession(false);
   var existing = c.Find<JournalDraft>(id);
   if (existing != null && (existing.UserId != user.UserId || existing.CompanyId != companyId)) throw new InvalidOperationException("Borrador no disponible.");
   if (expectedRevision != null && (existing?.Revision ?? 0) != expectedRevision) throw new InvalidOperationException("El borrador cambió en otra ventana. Sus cambios siguen en pantalla; no se sobrescribirá la otra versión.");
   revision = checked((existing?.Revision ?? 0) + 1);
   c.InsertOrReplace(new JournalDraft { Id = id, Revision = revision, UserId = user.UserId, CompanyId = companyId, JournalId = content.Header.Id,
    UpdatedUtc = DateTime.UtcNow, Payload = JsonSerializer.Serialize(content) });
   });
   return revision;
  }
 }
 public void DeleteJournalDraft(string id, int companyId, long? expectedRevision = null)
 {
  RequireCompany(companyId);
  var user = RequireSession();
  var c = db.GetConnection();
  using (c.Lock())
  {
   c.RunInTransaction(() =>
   {
   ValidateSession(false);
   var existing = c.Find<JournalDraft>(id);
   if (existing != null && existing.UserId == user.UserId && existing.CompanyId == companyId && expectedRevision != null && existing.Revision != expectedRevision)
    throw new InvalidOperationException("El borrador cambió en otra ventana; no se descartará esa versión.");
   c.Execute("DELETE FROM JournalDraft WHERE Id = ? AND UserId = ? AND CompanyId = ?", id, user.UserId, companyId);
   });
  }
 }
}
