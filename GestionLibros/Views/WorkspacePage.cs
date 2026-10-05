using GestionLibros.Data;
using GestionLibros.Models;
namespace GestionLibros.Views;
public class WorkspacePage : AccountingPage
{
 private readonly AppDatabase db;
 private readonly Picker companies = new() { Title = "Contabilidad" };
 private readonly Picker months = new() { Title = "Mes", WidthRequest = 100 };
 private readonly Entry year = new() { WidthRequest = 90, Keyboard = Keyboard.Numeric, MaxLength = 4 };
 public WorkspacePage(AppDatabase db) : base("Pantalla Principal", db, null, null, null, "Sistema de Contabilidad") {
  this.db = db;
  months.ItemsSource = Enumerable.Range(1, 12).Select(x => x.ToString("00")).ToList(); months.SelectedIndex = DateTime.Today.Month - 1; year.Text = DateTime.Today.Year.ToString();
  Gl2000Chrome.ApplyMenus(this, Open);

  var icons = new HorizontalStackLayout { Spacing = 2, Padding = new Thickness(0, 4) };
  foreach (var icon in Gl2000Chrome.StandardIcons(this, Open, () => { db.Logout(); Window!.Page = new LoginPage(db); return Task.CompletedTask; })) icons.Children.Add(icon);
  if (db.Session?.IsMaster == true) icons.Children.Add(Gl2000Chrome.IconButton("Usuarios", "🏢", () => Open("admin")));
  Body.Children.Insert(0, new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = icons });

  Body.Children.Add(AccountingUi.Label($"Usuario: {db.Session?.Username} · Nivel: {(db.Session?.IsMaster == true ? "Maestro" : "Operador")}"));
  Body.Children.Add(companies);
  var period = new HorizontalStackLayout { Spacing = 12 }; period.Children.Add(AccountingUi.Label("Mes")); period.Children.Add(months); period.Children.Add(AccountingUi.Label("Año")); period.Children.Add(year); Body.Children.Add(period);
  Body.Children.Add(Watermark());
  Body.Children.Add(AccountingUi.Label("Seleccione una contabilidad y un período para trabajar. Los asientos se consultan por el mes de su fecha."));
  Body.Children.Add(AccountingUi.Label("Pruebas locales · Acumulación, cierre anual, bancos y equivalencia con los reportes antiguos pendientes de validar.")); Body.Children.Add(Notice);
 }
 // Reproduces the "SISTEMAS FUSSIÓN" tiled watermark from the original main screen, using the real
 // gl_fusion5.png wallpaper from estilo.ini ([Basic3 Frame] WallPaper=FUSION5.bmp, WallPaperType=2 = tile).
 private static View Watermark() {
  var grid = new Grid { BackgroundColor = Gl2000Chrome.WorkspaceGray, HeightRequest = 260 };
  grid.Add(Gl2000Chrome.Tiled("gl_fusion5.png", 234, 127));
  grid.Add(new Border { BackgroundColor = Color.FromArgb("#55000000"), Padding = new Thickness(24, 16), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Stroke = Colors.Transparent, Content = new Label { Text = "FREDI\nCONTABILIDAD", FontSize = 42, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center } });
  return grid;
 }
 protected override async void OnAppearing() { base.OnAppearing(); await Guard(async () => { var id = (companies.SelectedItem as Company)?.Id; var list = await db.Companies(); companies.ItemsSource = list; companies.SelectedItem = list.FirstOrDefault(c => c.Id == id) ?? list.FirstOrDefault(); if (companies.SelectedItem is Company current) Title = $"FREDI Contabilidad - {current.Name}"; }); }
 private Task Open(string page) => Guard(async () => {
  if (page == "admin") { await Navigation.PushAsync(new AdminPage(db)); return; }
  if (companies.SelectedItem is not Company company) throw new InvalidOperationException("Seleccione o cree una contabilidad.");
  if (!int.TryParse(year.Text, out var y) || y < 1900 || y > 2100) throw new InvalidOperationException("Indique un año entre 1900 y 2100.");
  var m = months.SelectedIndex + 1;
  await Navigation.PushAsync(page switch { "catalog" => new CatalogPage(db, company), "journals" => new JournalsPage(db, company, y, m), "trial" => new TrialBalancePage(db, company, y, m), "ledger" => new LedgerPage(db, company, y, m), _ => new BalancesPage(db, company, y, m) });
 });
}
