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
 public JournalEditor(AppDatabase db, Company company, Journal journal) : base(journal.Id == 0 ? "Agregando un Asiento" : "Cambiando un Asiento") {
  this.db = db; this.company = company; original = journal;
  type.Text = journal.Type; reference.Text = journal.Reference; date.Date = journal.Date; concept.Text = journal.Concept;
  BuildClassicEditor();
 }
 protected override async void OnAppearing() { base.OnAppearing(); if (Window != null) ClassicWorkspaceChrome.WindowTitle(Window, Title); await Guard(async () => { if (!loaded) { accounts = await db.Accounts(company.Id); if (original.Id != 0) lines.AddRange(await db.Lines(company.Id, original.Id)); loaded = true; } Render(); }); }
 private Task EditLine(JournalLine? line) => Guard(() => PushLine(line));
 private Task PushLine(JournalLine? line) => Navigation.PushAsync(new MovementEditor(accounts.Where(a => !accounts.Any(child => child.ParentCode == a.Code)).ToList(), line, concept.Text ?? "", result => { if (line != null) lines.Remove(line); lines.Add(result); selected = null; Render(); }));
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
public class MovementEditor : AccountingPage
{
 public MovementEditor(List<Account> accounts, JournalLine? line, string defaultConcept, Action<JournalLine> accept) : base(line == null ? "Agregando movimiento" : "Cambiando movimiento", "form") {
  Body.BackgroundColor = AccountingUi.Green;
  var account = new Picker { Title = "Cuenta", ItemsSource = accounts, ItemDisplayBinding = new Binding(nameof(Account.Description)) }; account.SelectedItem = accounts.FirstOrDefault(a => a.Id == line?.AccountId);
  var accountCode = AccountingUi.Label("Seleccione una cuenta de detalle."); account.SelectedIndexChanged += (_, _) => accountCode.Text = (account.SelectedItem as Account)?.Code ?? "";
  var concept = new Entry { Placeholder = "Concepto", Text = line?.Concept ?? defaultConcept, MaxLength = 300 };
  var side = new Picker { Title = "Cargo o crédito", ItemsSource = new[] { "Cargo", "Crédito" }, SelectedIndex = line?.CreditCents > 0 ? 1 : 0 };
  var amount = new Entry { Placeholder = "Valor", Keyboard = Keyboard.Numeric, Text = line == null ? "" : ((line.DebitCents + line.CreditCents) / 100m).ToString("F2") };
  foreach (var view in new View[] { account, accountCode, concept, side, amount }) Body.Children.Add(view);
  Body.Children.Add(AccountingUi.Button("Aceptar movimiento", () => Guard(async () => {
   if (account.SelectedItem is not Account selected) throw new InvalidOperationException("Seleccione una cuenta.");
   if (!decimal.TryParse(amount.Text, System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.CurrentCulture, out var value) || value <= 0 || decimal.Round(value, 2) != value || value > 9999999999.99m) throw new InvalidOperationException("Indique un valor positivo con hasta 2 decimales, sin separadores de miles.");
   var cents = decimal.ToInt64(value * 100); accept(new JournalLine { AccountId = selected.Id, Concept = concept.Text ?? "", DebitCents = side.SelectedIndex == 0 ? cents : 0, CreditCents = side.SelectedIndex == 1 ? cents : 0 }); await Navigation.PopAsync();
  })));
  Body.Children.Add(AccountingUi.Button("Cancelar", async () => { await Navigation.PopAsync(); })); Body.Children.Add(Notice);
 }
}
