using GestionLibros.Data;
using GestionLibros.Models;
namespace GestionLibros.Views;
public class AdminPage : AccountingPage
{
 private readonly AppDatabase db;
 private readonly Picker company = new() { Title = "Contabilidad para el usuario" };
 private readonly Label users = AccountingUi.Label("");
 public AdminPage(AppDatabase db) : base("Empresas y Usuarios", db, null, null, null) {
  this.db = db;
  var name = new Entry { Placeholder = "Nueva contabilidad", MaxLength = 120 };
  var username = new Entry { Placeholder = "Usuario", MaxLength = 60 };
  var password = new Entry { Placeholder = "Contraseña (mínimo 10 caracteres)", IsPassword = true };
  var confirmation = new Entry { Placeholder = "Confirmar contraseña", IsPassword = true };
  Body.Children.Add(name); Body.Children.Add(AccountingUi.Button("Crear contabilidad", () => Guard(async () => { await db.AddCompany(name.Text ?? ""); name.Text = ""; await Load(); })));
  foreach (var view in new View[] { company, username, password, confirmation }) Body.Children.Add(view);
  Body.Children.Add(AccountingUi.Button("Crear usuario", () => Guard(async () => {
   if (company.SelectedItem is not Company selected) throw new InvalidOperationException("Seleccione una contabilidad.");
   if (password.Text != confirmation.Text) throw new InvalidOperationException("Las contraseñas no coinciden.");
   await db.AddUser(username.Text ?? "", password.Text ?? "", selected.Id); username.Text = password.Text = confirmation.Text = ""; await Load(); Notice.Text = "Usuario creado.";
  })));
  Body.Children.Add(users);
  Body.Children.Add(AccountingUi.Button("Salir", async () => await Navigation.PopAsync()));
  Body.Children.Add(Notice);
 }
 protected override async void OnAppearing() { base.OnAppearing(); await Guard(Load); }
 private async Task Load() { var previous = (company.SelectedItem as Company)?.Id; var list = await db.Companies(); company.ItemsSource = list; company.SelectedItem = list.FirstOrDefault(c => c.Id == previous) ?? list.FirstOrDefault(); users.Text = string.Join(Environment.NewLine, await db.Users()); }
}
