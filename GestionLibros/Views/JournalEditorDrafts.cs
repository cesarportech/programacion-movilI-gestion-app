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
 // Draft snapshots are debounced and written off the UI thread: every key press used to serialize the
 // whole póliza twice and run a synchronous SQLite transaction (with fsync) on the UI thread. Now edits
 // restart a short timer; when typing pauses the snapshot is captured on the UI thread and written in
 // the background, one write at a time (draftGate keeps the revision sequence consistent).
 private static readonly TimeSpan DraftDelay = TimeSpan.FromMilliseconds(400);
 private IDispatcherTimer? draftTimer;
 private readonly SemaphoreSlim draftGate = new(1, 1);
 private void PersistDraft()
 {
  if (viewOnly || !loaded || restoring) return;
  if (draftTimer == null)
  {
   draftTimer = Dispatcher.CreateTimer(); draftTimer.Interval = DraftDelay; draftTimer.IsRepeating = false;
   draftTimer.Tick += async (_, _) => await FlushDraft();
  }
  draftTimer.Stop(); draftTimer.Start();
 }
 private async Task FlushDraft()
 {
  draftTimer?.Stop();
  if (viewOnly || !loaded || restoring) return;
  await draftGate.WaitAsync();
  try
  {
   // Captured after taking the turn (back on the UI thread), so a save that finished meanwhile is seen.
   if (viewOnly || restoring || saving) return;
   var content = Capture(); var reference = baseline; var id = draftId; var revision = draftRevision;
   draftRevision = await Task.Run(() =>
   {
    if (JsonSerializer.Serialize(content) != reference) return db.StoreJournalDraft(id, company.Id, content, revision);
    db.DeleteJournalDraft(id, company.Id, revision); return 0L;
   });
  }
  catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Notice.Text = "No se pudo guardar el borrador de recuperación. Mantenga la ventana abierta."; }
  finally { draftGate.Release(); }
 }
 private async Task RecoverDraft()
 {
  var drafts = await Task.Run(() => db.JournalDrafts(company.Id, original.Id));
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
  draftTimer?.Stop();
  await draftGate.WaitAsync(); // a background draft write must finish before saving
  try
  {
  if (!await movementDialog.CommitPending()) return false;
  if (date.Date == null) throw new InvalidOperationException("Indique una fecha.");
  var content = Capture(); var id = draftId; var revision = draftRevision;
  draftRevision = await Task.Run(() => db.StoreJournalDraft(id, company.Id, content, revision));
  var header = Capture().Header;
  IsEnabled = false;
  await db.SaveCapturedJournal(company.Id, header, lines, draftId, draftRevision);
  draftRevision = 0;
  original.Id = header.Id; original.ChangedAt = header.ChangedAt;
  baseline = Fingerprint();
  draftTimer?.Stop(); // edits made while saving are already part of the saved póliza
  return true;
  }
  finally { saving = false; IsEnabled = true; draftGate.Release(); }
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
   draftTimer?.Stop();
   await draftGate.WaitAsync();
   try
   {
    var id = draftId; var revision = draftRevision;
    await Task.Run(() => db.DeleteJournalDraft(id, company.Id, revision));
    draftRevision = 0;
   }
   finally { draftGate.Release(); }
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

