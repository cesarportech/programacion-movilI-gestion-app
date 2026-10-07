using GestionLibros.Data;
using GestionLibros.Models;

namespace GestionLibros.Views;

public class WorkspacePage : AccountingPage
{
    private readonly AppDatabase db;
    private readonly Picker months = new() { WidthRequest = 51, HeightRequest = 20, MinimumHeightRequest = 0,
        FontFamily = "Arial", FontSize = 11, FontAutoScalingEnabled = false, TextColor = Colors.Black,
        BackgroundColor = Colors.White, Margin = 0 };
    private readonly Entry year = new() { WidthRequest = 44, HeightRequest = 20, MinimumHeightRequest = 0,
        FontFamily = "Arial", FontSize = 12, FontAutoScalingEnabled = false, TextColor = Colors.Black,
        BackgroundColor = Colors.Transparent, Keyboard = Keyboard.Numeric, MaxLength = 4, Margin = 0 };
    private List<Company> availableCompanies = new();
    private Company? company;

    public WorkspacePage(AppDatabase db) : base("Pantalla Principal")
    {
        this.db = db;
        NavigationPage.SetHasNavigationBar(this, false);
        BackgroundColor = Gl2000Chrome.WorkspaceGray;
        months.ItemsSource = Enumerable.Range(1, 12).Select(x => x.ToString("00")).ToList();
        months.SelectedIndex = DateTime.Today.Month - 1;
        year.Text = DateTime.Today.Year.ToString();
        SemanticProperties.SetDescription(months, "Mes de trabajo");
        SemanticProperties.SetDescription(year, "Año de trabajo");
        ClassicWorkspaceChrome.CompactPeriod(months, year);

        Gl2000Chrome.ApplyMenus(this, Open);
        var archivos = MenuBarItems[0];
        var exit = archivos.OfType<MenuFlyoutItem>().First(x => x.Text == "Salir");
        exit.Command = new Command(CloseApp);
        var menu = ClassicWorkspaceChrome.Menu(this, MenuBarItems.ToArray());
        // One compact menu row, without the NavigationPage heading.
        MenuBarItems.Clear();

        var tools = new HorizontalStackLayout { Spacing = 1, Padding = new Thickness(3, 2, 0, 0) };
        tools.Children.Add(ClassicWorkspaceChrome.Tool("Catálogo", "gl_catalog.png", () => Open("catalog")));
        tools.Children.Add(ClassicWorkspaceChrome.Tool("Asientos", "gl_journals.png", () => Open("journals")));
        tools.Children.Add(ClassicWorkspaceChrome.Tool("Acumular", "gl_accumulate.png", () => MenuActions.Accumulate(this)));
        tools.Children.Add(ClassicWorkspaceChrome.Tool("Consulta", "gl_query.png", () => Open("balances")));
        tools.Children.Add(ClassicWorkspaceChrome.Tool("Balanza", "gl_trial.png", () => Open("trial")));
        tools.Children.Add(ClassicWorkspaceChrome.Tool("Saldos", "gl_balances.png", () => Open("saldos")));
        tools.Children.Add(ClassicWorkspaceChrome.Tool("Mayor", "gl_ledger.png", () => Open("ledger")));
        tools.Children.Add(ClassicWorkspaceChrome.Tool("Salir", "gl_exit.png", () => { CloseApp(); return Task.CompletedTask; }));
        tools.Children.Add(ClassicWorkspaceChrome.SignOutTool(() => SessionActions.SignOut(this, db)));
        var period = new HorizontalStackLayout { Spacing = 5, Margin = new Thickness(67, 4, 0, 0), VerticalOptions = LayoutOptions.Start };
        period.Children.Add(ClassicWorkspaceChrome.Text("Mes", true));
        period.Children.Add(months);
        period.Children.Add(ClassicWorkspaceChrome.Text("Año", true));
        period.Children.Add(year);
        tools.Children.Add(period);
        var toolbar = new Border { HeightRequest = 57, Padding = 0, Stroke = Colors.Gray,
            StrokeThickness = 1, Background = ClassicWorkspaceChrome.ToolbarBrush(),
            Content = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = tools } };

        var wallpaper = new Grid { BackgroundColor = Gl2000Chrome.WorkspaceGray, IsClippedToBounds = true };
        wallpaper.Add(Gl2000Chrome.Tiled("gl_fusion5.png", 234, 127));
        Notice.FontFamily = "Arial";
        Notice.FontSize = 12;
        Notice.Padding = new Thickness(8, 4);
        Notice.BackgroundColor = Colors.LightYellow;
        Notice.VerticalOptions = LayoutOptions.Start;
        Notice.IsVisible = false;
        Notice.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Label.Text)) Notice.IsVisible = !string.IsNullOrEmpty(Notice.Text); };
        wallpaper.Add(Notice);
        var layout = new Grid { RowSpacing = 0, RowDefinitions = {
            new RowDefinition(20), new RowDefinition(57), new RowDefinition(GridLength.Star), new RowDefinition(21) } };
        layout.Add(menu, 0, 0);
        layout.Add(toolbar, 0, 1);
        layout.Add(wallpaper, 0, 2);
        layout.Add(ClassicWorkspaceChrome.Status(db), 0, 3);
        Content = layout;
        Loaded += (_, _) => UpdateTitle();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Guard(async () =>
        {
            availableCompanies = await db.Companies();
            company = availableCompanies.FirstOrDefault(c => c.Id == company?.Id) ?? availableCompanies.FirstOrDefault();
            UpdateTitle();
        });
    }

    private void UpdateTitle()
    {
        Title = "Sistema de Contabilidad (GL 2000)" + (company == null ? "" : $" - {company.Name}");
        if (Window != null) ClassicWorkspaceChrome.WindowTitle(Window, Title);
    }

    // Salir: closes the program like the GL2000, keeping the session for the next start.
    private void CloseApp()
    {
#if WINDOWS || MACCATALYST
        if (Window != null) Application.Current?.CloseWindow(Window);
#else
        Application.Current?.Quit();
#endif
    }

    private Task Open(string page) => Guard(async () =>
    {
        if (page == "logout") { await SessionActions.SignOut(this, db); return; }
        if (page == "admin") { await MenuActions.AdminOnly(this); return; }
        if (company == null) throw new InvalidOperationException("Seleccione o cree una contabilidad.");
        if (!int.TryParse(year.Text, out var y) || y < 1900 || y > 2100)
            throw new InvalidOperationException("Indique un año entre 1900 y 2100.");
        var m = months.SelectedIndex + 1;
        if (m < 1 || m > 12) throw new InvalidOperationException("Seleccione un mes.");
        // Reports, utilities and notices open on top of this screen.
        if (await MenuActions.TryRun(this, db, company, y, m, page)) return;
        await Navigation.PushAsync(page switch {
            "catalog" => new CatalogPage(db, company, y, m), "journals" => new JournalsPage(db, company, y, m),
            _ => new BalancesPage(db, company, y, m) });
    });
}
