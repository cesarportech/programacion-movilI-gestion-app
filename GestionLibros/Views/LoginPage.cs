using GestionLibros.Data;
namespace GestionLibros.Views;
public class LoginPage : ContentPage
{
 private readonly AppDatabase database;
 private readonly Entry username = new() { Placeholder = "Usuario", AutomationId = "Username" };
 private readonly Entry password = new() { Placeholder = "Contraseña", IsPassword = true, AutomationId = "Password" };
 private readonly Entry confirm = new() { Placeholder = "Confirmar contraseña", IsPassword = true };
 private readonly Label instructions = new();
 private readonly Label message = new() { TextColor = Colors.Firebrick };
 private readonly Button submit = new() { Text = "Ingresar", IsEnabled = false };
 private bool setup;
 public LoginPage(AppDatabase database)
 {
  this.database = database;
  Title = "FREDI · Contabilidad";
  var form = new VerticalStackLayout { Spacing = 16, Padding = 28, MaximumWidthRequest = 460, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
  form.Children.Add(new Label { Text = "FREDI", FontSize = 36, FontAttributes = FontAttributes.Bold });
  form.Children.Add(new Label { Text = "Sistema de contabilidad", FontSize = 22 });
  foreach (var view in new View[] { instructions, username, password, confirm, submit, message }) form.Children.Add(view);
  form.Children.Add(new Label { Text = "Pruebas locales · Datos independientes del sistema antiguo", FontSize = 12 });
  var card = new Border { Content = form, BackgroundColor = Color.FromArgb("#F2F2F2"), Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0 };
  var centered = new Grid { Children = { card } };
  var (file, w, h) = Gl2000Chrome.Texture("splash");
  Content = Gl2000Chrome.Backdrop(new ScrollView { Content = centered }, file, w, h);
  submit.Clicked += SignIn;
 }
 protected override async void OnAppearing()
 {
  base.OnAppearing();
  try { setup = await database.NeedsSetup(); confirm.IsVisible = setup; submit.Text = setup ? "Crear usuario maestro" : "Ingresar"; instructions.Text = setup ? "Primera ejecución: cree su usuario maestro. Contraseña de al menos 10 caracteres." : "Ingrese con el usuario asignado a su contabilidad."; submit.IsEnabled = true; }
  catch { message.Text = "No se pudo abrir la base local. Verifique el acceso a la carpeta de datos."; }
 }
 private async void SignIn(object? sender, EventArgs e)
 {
  submit.IsEnabled = false; message.Text = "";
  try {
   if (setup) {
    if (password.Text != confirm.Text) throw new InvalidOperationException("Las contraseñas no coinciden.");
    await database.Setup(username.Text ?? "", password.Text ?? ""); setup = false; confirm.IsVisible = false;
   }
   await database.Login(username.Text ?? "", password.Text ?? "");
   password.Text = confirm.Text = "";
   Window!.Page = new NavigationPage(new WorkspacePage(database));
  } catch (InvalidOperationException ex) { message.Text = ex.Message; }
  catch { message.Text = "No se pudo completar el ingreso. Inténtelo nuevamente."; }
  finally { submit.IsEnabled = true; }
 }
}
