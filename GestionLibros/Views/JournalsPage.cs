using GestionLibros.Data;
using GestionLibros.Models;
namespace GestionLibros.Views;
public partial class JournalEditor : AccountingPage
{
 private readonly AppDatabase db; private readonly Company company; private readonly Journal original;
 private readonly Entry type = new() { Placeholder = "Tipo de póliza", MaxLength = 20 };
 private readonly Entry reference = new() { Placeholder = "Referencia", MaxLength = 60 };
 private readonly DatePicker date = new(); private readonly Entry concept = new() { Placeholder = "Concepto", MaxLength = 300 };
 private readonly VerticalStackLayout rows = new() { Spacing = 1, MinimumWidthRequest = 850 };
 private readonly Label sums = AccountingUi.Label("Sumas iguales", true); private readonly Label selection = AccountingUi.Label("Seleccione un movimiento.");
 private List<Account> accounts = []; private readonly List<JournalLine> lines = []; private JournalLine? selected; private bool loaded;
 private readonly MovementDialog movementDialog;
 public JournalEditor(AppDatabase db, Company company, Journal journal) : base(journal.Id == 0 ? "Agregando un Asiento" : "Cambiando un Asiento") {
  this.db = db; this.company = company; original = journal;
  movementDialog = new MovementDialog(this);
  type.Text = journal.Type; reference.Text = journal.Reference; date.Date = journal.Date; concept.Text = journal.Concept;
  BuildClassicEditor();
 }
 protected override async void OnAppearing() { base.OnAppearing(); if (Window != null) ClassicWorkspaceChrome.WindowTitle(Window, Title); await Guard(async () => { if (!loaded) { accounts = await db.Accounts(company.Id); policyTypes = await db.PolicyTypes(company.Id); ShowTypeName(); if (original.Id != 0) lines.AddRange(await db.Lines(company.Id, original.Id)); loaded = true; } Render(); }); }
 private Task EditLine(JournalLine? line) => Guard(() => PushLine(line));
 private Task PushLine(JournalLine? line)
 {
  var parents = accounts.Select(a => a.ParentCode).ToHashSet();
  var title = $"{(line == null ? "Agregando" : "Cambiando")} : {(type.Text ?? "").Trim()}-{(reference.Text ?? "").Trim()}";
  movementDialog.Show(title, accounts.Where(a => !parents.Contains(a.Code)).ToList(), line, concept.Text ?? "", result =>
  {
   // A changed line keeps its position, like the original grid.
   var index = line == null ? -1 : lines.IndexOf(line);
   if (index >= 0) lines[index] = result; else lines.Add(result);
   selected = result; Render();
  });
  return Task.CompletedTask;
 }
 private void Render() {
  rows.Children.Clear();
  foreach (var line in lines) {
   var account = accounts.FirstOrDefault(a => a.Id == line.AccountId);
   rows.Children.Add(MovementRow([account?.Code ?? "?", account?.Description ?? "?", line.Concept, AccountingUi.Money(line.DebitCents), AccountingUi.Money(line.CreditCents)], line));
  }
  var debit = lines.Sum(l => l.DebitCents); var credit = lines.Sum(l => l.CreditCents);
  sums.Text = debit == credit ? "Sumas Iguales :" : "Diferencia: " + AccountingUi.Money(debit-credit);
  sums.TextColor = debit == credit ? Colors.Blue : Colors.DarkRed;
  debitTotal.Text = AccountingUi.Money(debit); creditTotal.Text = AccountingUi.Money(credit);
 }
}
