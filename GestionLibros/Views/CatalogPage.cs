using GestionLibros.Data;
using GestionLibros.Models;
namespace GestionLibros.Views;
public class CatalogPage : AccountingPage
{
 private readonly AppDatabase db; private readonly Company company;
 private readonly VerticalStackLayout rows = new() { Spacing = 1, MinimumWidthRequest = 850 };
 private readonly Entry search = new() { Placeholder = "Localizador: número o descripción" };
 private readonly Label selectedLabel = AccountingUi.Label("Seleccione una cuenta.");
 private List<Account> accounts = []; private Account? selected;
 public CatalogPage(AppDatabase db, Company company) : base("Catálogo de Cuentas", db, company, DateTime.Today.Year, DateTime.Today.Month, "Localizador") {
  this.db = db; this.company = company;
  search.TextChanged += (_, _) => Render(); Body.Children.Add(search); Body.Children.Add(AccountingUi.Table(rows)); Body.Children.Add(selectedLabel);
  var actions = new HorizontalStackLayout { Spacing = 8 };
  actions.Children.Add(AccountingUi.Button("Agregar", () => Guard(() => Navigation.PushAsync(new AccountEditor(db, company, null)))));
  actions.Children.Add(AccountingUi.Button("Cambiar", () => Guard(async () => { if (selected == null) throw new InvalidOperationException("Seleccione una cuenta."); await Navigation.PushAsync(new AccountEditor(db, company, selected)); })));
  actions.Children.Add(AccountingUi.Button("Borrar", () => Guard(async () => { if (selected == null) throw new InvalidOperationException("Seleccione una cuenta."); if (await DisplayAlertAsync("Borrar cuenta", $"¿Borrar {selected.Code} — {selected.Description}?", "Borrar", "Cancelar")) { await db.DeleteAccount(company.Id, selected.Id); await Load(); } })));
  actions.Children.Add(AccountingUi.Button("Salir", async () => await Navigation.PopAsync()));
  actions.Children.Add(AccountingUi.Button("Ayuda", () => DisplayAlertAsync("Ayuda", "Localice una cuenta y use Agregar, Cambiar o Borrar.", "Cerrar")));
  Body.Children.Add(actions); Body.Children.Add(Notice);
 }
 protected override async void OnAppearing() { base.OnAppearing(); await Guard(Load); }
 private async Task Load() { accounts = await db.Accounts(company.Id); selected = null; selectedLabel.Text = "Seleccione una cuenta."; Render(); }
 private void Render() { rows.Children.Clear(); rows.Children.Add(AccountingUi.Row(["Cuenta", "Descripción", "Cta. superior", "Nivel"], true)); var term = search.Text ?? ""; foreach (var a in accounts.Where(a => a.Code.Contains(term, StringComparison.OrdinalIgnoreCase) || a.Description.Contains(term, StringComparison.OrdinalIgnoreCase))) { var account = a; rows.Children.Add(AccountingUi.Row([account.Code, account.Description, account.ParentCode, account.Level.ToString()], selected: selected?.Id == account.Id, linkColumns: 2, select: () => { selected = account; selectedLabel.Text = $"Seleccionada: {account.Code} — {account.Description}"; Render(); })); } }
}
public class AccountEditor : AccountingPage
{
 public AccountEditor(AppDatabase db, Company company, Account? account) : base(account == null ? "Agregando una cuenta" : "Cambiando una cuenta", db, company, null, null, texture: "form") {
  var code = new Entry { Placeholder = "Cuenta", Text = account?.Code, IsEnabled = account == null };
  var description = new Entry { Placeholder = "Descripción", Text = account?.Description };
  var parent = new Entry { Placeholder = "Cuenta superior (opcional)", Text = account?.ParentCode, IsEnabled = account == null };
  Body.BackgroundColor = AccountingUi.Green;
  Body.Children.Add(code); Body.Children.Add(description); Body.Children.Add(parent);
  if (account != null) Body.Children.Add(AccountingUi.Label("En esta etapa se modifica la descripción. El código y la jerarquía se conservan."));
  Body.Children.Add(AccountingUi.Button("Aceptar", () => Guard(async () => { if (account == null) await db.AddAccount(company.Id, code.Text ?? "", description.Text ?? "", parent.Text ?? ""); else await db.UpdateAccount(company.Id, account.Id, description.Text ?? ""); await Navigation.PopAsync(); })));
  Body.Children.Add(AccountingUi.Button("Cancelar", async () => { await Navigation.PopAsync(); })); Body.Children.Add(Notice);
 }
}
