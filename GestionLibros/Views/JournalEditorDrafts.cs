using GestionLibros.Models;
using System.Text.Json;
namespace GestionLibros.Views;
public partial class JournalEditor
{
 private string draftId = Guid.NewGuid().ToString("N");
 private string baseline = "";
 private long draftRevision;
 private bool saving;
 private bool restoring, deciding;
 private int editingIndex = -1;
 private JournalDraftContent Capture() => new(new Journal { Id = original.Id, ChangedAt = original.ChangedAt,
  Type = type.Text ?? "", Reference = reference.Text ?? "", Date = date.Date ?? DateTime.MinValue, Concept = concept.Text ?? "" },
  lines.ToList(), movementDialog.HasChanges ? movementDialog.Capture(editingIndex) : null);
 private string Fingerprint() => JsonSerializer.Serialize(Capture());
 public bool HasPendingChanges => !viewOnly && loaded && Fingerprint() != baseline;
 private void TrackChanges()
 {
  foreach (var field in new[] { type, reference, concept }) field.TextChanged += (_, _) => PersistDraft();
  date.DateSelected += (_, _) => PersistDraft();
  movementDialog.Changed += PersistDraft;
 }
 private void PersistDraft()
 {
  if (viewOnly || !loaded || restoring) return;
  try
  {
   if (HasPendingChanges) draftRevision = db.StoreJournalDraft(draftId, company.Id, Capture(), draftRevision);
   else { db.DeleteJournalDraft(draftId, company.Id, draftRevision); draftRevision = 0; }
  }
  catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Notice.Text = "No se pudo guardar el borrador de recuperación. Mantenga la ventana abierta."; }
 }
 private async Task RecoverDraft()
 {
  var drafts = db.JournalDrafts(company.Id, original.Id);
  if (drafts.Count == 0) return;
  var labels = drafts.Select(d => $"{d.UpdatedUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss} · {d.Id}").ToArray();
  var choice = await DisplayActionSheetAsync("Borradores pendientes (no afectan saldos)", "Abrir sin recuperar", null, labels);
  var index = Array.IndexOf(labels, choice);
  if (index < 0) return; // Existing drafts remain until explicitly discarded or accepted.
  var draft = drafts[index];
  var content = JsonSerializer.Deserialize<JournalDraftContent>(draft.Payload) ?? throw new InvalidOperationException("Borrador no válido.");
  restoring = true;
  try
  {
   draftId = draft.Id; draftRevision = draft.Revision;
   original.ChangedAt = content.Header.ChangedAt;
   type.Text = content.Header.Type; reference.Text = content.Header.Reference; concept.Text = content.Header.Concept;
   date.Date = content.Header.Date == DateTime.MinValue ? null : content.Header.Date;
   lines.Clear(); lines.AddRange(content.Lines);
   if (content.Movement != null)
   {
    var i = content.Movement.Index;
    await PushLine(i >= 0 && i < lines.Count ? lines[i] : null);
    movementDialog.Restore(content.Movement);
   }
   Render(); Notice.Text = "Borrador recuperado. Revise y acepte la póliza para afectar saldos.";
  }
  finally { restoring = false; }
 }
 private async Task<bool> SavePending()
 {
  if (viewOnly || saving) return false;
  saving = true;
  try
  {
  if (!await movementDialog.CommitPending()) return false;
  if (date.Date == null) throw new InvalidOperationException("Indique una fecha.");
  draftRevision = db.StoreJournalDraft(draftId, company.Id, Capture(), draftRevision);
  var header = Capture().Header;
  IsEnabled = false;
  await db.SaveJournal(company.Id, header, lines, draftId, draftRevision);
  draftRevision = 0;
  original.Id = header.Id; original.ChangedAt = header.ChangedAt;
  baseline = Fingerprint();
  return true;
  }
  finally { saving = false; IsEnabled = true; }
 }
 public async Task<bool> ConfirmLeave(Page prompt)
 {
  if (deciding || saving) return false;
  if (!HasPendingChanges) return true;
  deciding = true;
  try
  {
   var choice = await prompt.DisplayActionSheetAsync("Póliza con cambios sin guardar", "Continuar editando", "Descartar", "Guardar");
   if (choice == "Guardar") return await SavePending();
   if (choice != "Descartar") return false;
   db.DeleteJournalDraft(draftId, company.Id, draftRevision);
   draftRevision = 0;
   restoring = true;
   try
   {
    var previous = JsonSerializer.Deserialize<JournalDraftContent>(baseline)!;
    original.ChangedAt = previous.Header.ChangedAt;
    movementDialog.Abandon();
    type.Text = previous.Header.Type; reference.Text = previous.Header.Reference; concept.Text = previous.Header.Concept;
    date.Date = previous.Header.Date == DateTime.MinValue ? null : previous.Header.Date;
    lines.Clear(); lines.AddRange(previous.Lines); Render();
   }
   finally { restoring = false; }
   baseline = Fingerprint();
   return true;
  }
  catch (Exception ex) { await prompt.DisplayAlertAsync("No se pudo guardar o salir", ex.Message, "Continuar editando"); return false; }
  finally { deciding = false; }
 }
 private async Task Leave() { if (await ConfirmLeave(this)) await Navigation.PopAsync(); }
 protected override bool OnBackButtonPressed() { _ = Leave(); return true; }
}

internal static class PendingEdits
{
 internal static async Task CloseWindow(Window? window)
 {
  if (window == null || !await Confirm(window)) return;
  Application.Current?.CloseWindow(window);
 }
 internal static async Task<bool> Confirm(Window? window)
 {
  if (window?.Page == null) return true;
  var pages = window.Page is NavigationPage nav ? nav.Navigation.NavigationStack : new[] { window.Page };
  foreach (var editor in pages.OfType<JournalEditor>().Reverse().ToArray())
   if (!await editor.ConfirmLeave(window.Page)) return false;
  return true;
 }
}
