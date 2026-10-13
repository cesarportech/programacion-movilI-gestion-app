using GestionLibros.Data;
using GestionLibros.Models;
namespace GestionLibros.Views;

internal static class CaptureShortcut
{
 public static void F12(VisualElement field, Func<Task> action)
    => Bind(field, 123, action);
 public static void Enter(VisualElement field, Func<Task> action)
    => Bind(field, 13, action);
 private static void Bind(VisualElement field, int key, Func<Task> action)
 {
#if WINDOWS
  Microsoft.UI.Xaml.UIElement? attached = null;
  bool busy = false;
  var handler = new Microsoft.UI.Xaml.Input.KeyEventHandler(async (_, e) =>
  {
   if ((int)e.Key != key || busy) return;
   e.Handled = true; busy = true;
   try { await action(); } finally { busy = false; }
  });
  void Attach()
  {
   if (attached != null) attached.RemoveHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent, handler);
   attached = field.Handler?.PlatformView as Microsoft.UI.Xaml.UIElement;
   attached?.AddHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent, handler, true);
  }
  field.HandlerChanged += (_, _) => Attach(); Attach();
#endif
 }
}

public partial class JournalEditor
{
 private string? suggestedReference;
 private int referenceRequest;
 private string referenceType = "";
 private async Task RefreshReference() { try { await ProposeReference(false); } catch (InvalidOperationException ex) { Notice.Text = ex.Message; } catch { Notice.Text = "No se pudo proponer la referencia."; } }
 private bool proposingReference;
 private void ConfigureCaptureShortcuts()
 {
  type.Completed += (_, _) => reference.Focus();
  reference.Completed += (_, _) => date.Focus();
  CaptureShortcut.Enter(date, () => { concept.Focus(); return Task.CompletedTask; });
  concept.Completed += async (_, _) => await EditLine(null);
  CaptureShortcut.F12(type, () => Guard(async () => { var last = await db.PreviousJournalCapture(company.Id); if (last != null) type.Text = last.Type; else Notice.Text = "No hay una captura anterior de este usuario."; }));
  CaptureShortcut.F12(concept, () => Guard(async () => { var last = await db.PreviousJournalCapture(company.Id); if (last != null) concept.Text = last.Concept; else Notice.Text = "No hay una captura anterior de este usuario."; }));
  CaptureShortcut.F12(date, () => Guard(async () => { var last = await db.PreviousJournalCapture(company.Id); if (last != null) date.Date = last.Date; }));
  CaptureShortcut.F12(reference, () => Guard(() => ProposeReference(true)));
  movementDialog.RepeatSaved = async () =>
  {
   db.ValidateSession(true);
   var last = await db.PreviousJournalCapture(company.Id);
   if (last == null) return null;
   var line = (await db.Lines(company.Id, last.Id)).LastOrDefault();
   var code = accounts.FirstOrDefault(a => a.Id == line?.AccountId)?.Code;
   return line == null || code == null ? null : new MovementDraft(code, line.Concept, ((line.DebitCents - line.CreditCents) / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), -1);
  };
  type.TextChanged += async (_, _) => { if (loaded && !restoring && !viewOnly) await RefreshReference(); };
  date.DateSelected += async (_, _) => { if (loaded && !restoring && !viewOnly) await RefreshReference(); };
  reference.TextChanged += (_, e) =>
  {
   if (proposingReference || viewOnly) return;
   var clean = new string((e.NewTextValue ?? "").Where(c => c >= '0' && c <= '9').Take(7).ToArray());
   if (clean != e.NewTextValue) reference.Text = clean;
  };
 }
 private async Task InitializeReference()
 {
  if (string.IsNullOrWhiteSpace(type.Text)) type.Text = (await db.PreviousJournalCapture(company.Id))?.Type ?? "";
  if (string.IsNullOrWhiteSpace(reference.Text)) await ProposeReference(false);
 }
 private async Task ProposeReference(bool force)
 {
  if (viewOnly || original.Id != 0 || restoring) return;
  var request = ++referenceRequest;
  var selectedType = type.Text?.Trim() ?? ""; var selectedDate = date.Date;
  if (selectedDate == null || selectedType.Length == 0) return;
  var current = reference.Text ?? "";
  var prefix = $"{selectedDate.Value.Year % 10}{selectedDate.Value.Month:00}";
  // Keep a manual suffix for this period; a changed year/month always needs a new prefix.
  if (!force && selectedType == referenceType && current.Length > 0 && current != suggestedReference && current.StartsWith(prefix, StringComparison.Ordinal)) return;
  var next = await db.NextJournalReference(company.Id, selectedType, selectedDate.Value);
  if (request != referenceRequest || selectedType != type.Text?.Trim() || selectedDate != date.Date || current != (reference.Text ?? "")) return;
  proposingReference = true;
  try { reference.Text = next; suggestedReference = next; referenceType = selectedType; }
  finally { proposingReference = false; }
 }
}

