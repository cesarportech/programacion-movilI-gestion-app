using GestionLibros.Data;
using GestionLibros.Models;
namespace GestionLibros.Views;
public class JournalsPage : AccountingPage
{
 private readonly AppDatabase db; private readonly Company company; private readonly int year; private int month;
 private readonly VerticalStackLayout rows = new() { Spacing = 1, MinimumWidthRequest = 1050 };
 private readonly Label selection = AccountingUi.Label("Seleccione una póliza."); private Journal? selected;
 private List<Journal> journals = [];
 public JournalsPage(AppDatabase db, Company company, int year, int month) : base("Tabla de Asientos", db, company, year, month, "Recorriendo Registros") {
  this.db = db; this.company = company; this.year = year; this.month = month;
  var tabs = new HorizontalStackLayout { Spacing = 3 }; for (var m = 1; m <= 12; m++) { var value = m; tabs.Children.Add(AccountingUi.Button(System.Globalization.CultureInfo.GetCultureInfo("es-HN").DateTimeFormat.GetAbbreviatedMonthName(m), () => Guard(async () => { this.month = value; await Load(); }))); }
  Body.Children.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = tabs }); Body.Children.Add(AccountingUi.Table(rows)); Body.Children.Add(selection);
  var actions = new HorizontalStackLayout { Spacing = 8 };
  actions.Children.Add(AccountingUi.Button("Agregar", () => Guard(() => Navigation.PushAsync(new JournalEditor(db, company, new Journal { Date = new DateTime(year, this.month, 1) })))));
  actions.Children.Add(AccountingUi.Button("Cambiar", () => Guard(async () => { if (selected == null) throw new InvalidOperationException("Seleccione una póliza."); await Navigation.PushAsync(new JournalEditor(db, company, selected)); })));
  actions.Children.Add(AccountingUi.Button("Borrar", () => Guard(async () => { if (selected == null) throw new InvalidOperationException("Seleccione una póliza."); if (await DisplayAlertAsync("Borrar póliza", $"¿Borrar {selected.Type}-{selected.Reference} y sus movimientos?", "Borrar", "Cancelar")) { await db.DeleteJournal(company.Id, selected.Id); await Load(); } })));
  actions.Children.Add(AccountingUi.Button("Salir", async () => await Navigation.PopAsync()));
  actions.Children.Add(AccountingUi.Button("Ayuda", () => DisplayAlertAsync("Ayuda", "Elija un mes, localice una póliza y use Agregar, Cambiar o Borrar.", "Cerrar")));
  Body.Children.Add(actions); Body.Children.Add(Notice);
 }
 protected override async void OnAppearing() { base.OnAppearing(); await Guard(Load); }
 private async Task Load() { selected = null; selection.Text = $"Mes: {month:00} · Seleccione una póliza."; journals = await db.Journals(company.Id, year, month); Render(); }
 private void Render() { rows.Children.Clear(); rows.Children.Add(AccountingUi.Row(["Tipo", "Referencia", "Fecha", "Concepto", "Usuario", "Fecha cambio"], true)); foreach (var journal in journals) rows.Children.Add(AccountingUi.Row([journal.Type, journal.Reference, journal.Date.ToString("dd/MM/yyyy"), journal.Concept, journal.ChangedBy, journal.ChangedAt.ToString("dd/MM/yyyy HH:mm")], selected: selected?.Id == journal.Id, select: () => { selected = journal; selection.Text = $"Seleccionada: {journal.Type}-{journal.Reference}"; Render(); })); }
}
public class JournalEditor : AccountingPage
{
 private readonly AppDatabase db; private readonly Company company; private readonly Journal original;
 private readonly Entry type = new() { Placeholder = "Tipo de póliza", MaxLength = 20 };
 private readonly Entry reference = new() { Placeholder = "Referencia", MaxLength = 60 };
 private readonly DatePicker date = new(); private readonly Entry concept = new() { Placeholder = "Concepto", MaxLength = 300 };
 private readonly VerticalStackLayout rows = new() { Spacing = 1, MinimumWidthRequest = 850 };
 private readonly Label sums = AccountingUi.Label("Sumas iguales", true); private readonly Label selection = AccountingUi.Label("Seleccione un movimiento.");
 private List<Account> accounts = []; private readonly List<JournalLine> lines = []; private JournalLine? selected; private bool loaded;
 public JournalEditor(AppDatabase db, Company company, Journal journal) : base(journal.Id == 0 ? "Agregando un Asiento" : "Cambiando un Asiento", db, company, journal.Date.Year, journal.Date.Month, texture: "form") {
  this.db = db; this.company = company; original = journal;
  Body.BackgroundColor = AccountingUi.Green; type.Text = journal.Type; reference.Text = journal.Reference; date.Date = journal.Date; concept.Text = journal.Concept;
  Body.Children.Add(AccountingUi.Label(company.Name, true)); foreach (var view in new View[] { type, reference, date, concept }) Body.Children.Add(view);
  Body.Children.Add(AccountingUi.Table(rows)); Body.Children.Add(selection); Body.Children.Add(sums);
  var actions = new HorizontalStackLayout { Spacing = 8 };
  actions.Children.Add(AccountingUi.Button("Agregar movimiento", () => EditLine(null)));
  actions.Children.Add(AccountingUi.Button("Cambiar movimiento", () => Guard(async () => { if (selected == null) throw new InvalidOperationException("Seleccione un movimiento."); await PushLine(selected); })));
  actions.Children.Add(AccountingUi.Button("Borrar movimiento", () => Guard(async () => { if (selected == null) throw new InvalidOperationException("Seleccione un movimiento."); if (await DisplayAlertAsync("Quitar movimiento", "¿Quitar el movimiento de esta póliza? Se guardará al aceptar la póliza.", "Quitar", "Cancelar")) { lines.Remove(selected); selected = null; Render(); } })));
  Body.Children.Add(actions);
  Body.Children.Add(AccountingUi.Button("Aceptar póliza", () => Guard(async () => { if (date.Date == null) throw new InvalidOperationException("Indique una fecha."); await db.SaveJournal(company.Id, new Journal { Id = original.Id, Date = date.Date.Value, Type = type.Text ?? "", Reference = reference.Text ?? "", Concept = concept.Text ?? "" }, lines); await Navigation.PopAsync(); })));
  Body.Children.Add(AccountingUi.Button("Cancelar", async () => { await Navigation.PopAsync(); })); Body.Children.Add(Notice);
 }
 protected override async void OnAppearing() { base.OnAppearing(); await Guard(async () => { if (!loaded) { accounts = await db.Accounts(company.Id); if (original.Id != 0) lines.AddRange(await db.Lines(company.Id, original.Id)); loaded = true; } Render(); }); }
 private Task EditLine(JournalLine? line) => Guard(() => PushLine(line));
 private Task PushLine(JournalLine? line) => Navigation.PushAsync(new MovementEditor(accounts.Where(a => !accounts.Any(child => child.ParentCode == a.Code)).ToList(), line, concept.Text ?? "", result => { if (line != null) lines.Remove(line); lines.Add(result); selected = null; Render(); }));
 private void Render() { rows.Children.Clear(); rows.Children.Add(AccountingUi.Row(["Cuenta", "Descripción", "Concepto", "Cargos", "Créditos"], true)); foreach (var line in lines) { var account = accounts.FirstOrDefault(a => a.Id == line.AccountId); rows.Children.Add(AccountingUi.Row([account?.Code ?? "?", account?.Description ?? "?", line.Concept, AccountingUi.Money(line.DebitCents), AccountingUi.Money(line.CreditCents)], selected: selected == line, linkColumns: 2, select: () => { selected = line; selection.Text = $"Seleccionado: {account?.Code} · {line.Concept}"; Render(); })); } var debit = lines.Sum(l => l.DebitCents); var credit = lines.Sum(l => l.CreditCents); sums.Text = $"{(debit == credit ? "Sumas iguales" : "Diferencia: " + AccountingUi.Money(debit-credit))}     Cargos: {AccountingUi.Money(debit)}     Créditos: {AccountingUi.Money(credit)}"; sums.TextColor = debit == credit ? Colors.DarkBlue : Colors.DarkRed; }
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
