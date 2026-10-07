using GestionLibros.Data;
using GestionLibros.Models;

namespace GestionLibros.Views;

// Home screen of the master user: companies (contabilidades) and their users. The master never opens an
// accounting; each company is worked by the user assigned to it.
public class AdminPage : AccountingPage
{
    private readonly AppDatabase db;
    private readonly CollectionView companies = List();
    private readonly CollectionView users = List();
    private readonly Entry companyName = Field("Nueva contabilidad", 120);
    private readonly Entry username = Field("Usuario", 60);
    private readonly Entry password = Field("Contraseña", 100, secret: true);
    private readonly Entry confirmation = Field("Confirmar contraseña", 100, secret: true);
    private readonly Picker userCompany = new() { FontFamily = "Arial", FontSize = 11, TextColor = Colors.Black, BackgroundColor = Colors.White,
        HeightRequest = 22, MinimumHeightRequest = 0 };
    private readonly Label status = ClassicWorkspaceChrome.Text("");
    private List<Company> companyList = [];
    private UserInfo? selectedUser;

    public AdminPage(AppDatabase db) : base("Empresas y Usuarios")
    {
        this.db = db;
        NavigationPage.SetHasNavigationBar(this, false);
        Title = "Sistema de Contabilidad (GL 2000) - Administración [Empresas y Usuarios]";
        SemanticProperties.SetDescription(userCompany, "Contabilidad del usuario");
        ClassicWorkspaceChrome.CompactPicker(userCompany);

        // Master menu: only administration; no accounting modules.
        var archivos = new MenuBarItem { Text = "Archivos" };
        archivos.Add(new MenuFlyoutItem { Text = "Usuarios Conectados", Command = new Command(async () => await Run(ShowSessions)) });
        archivos.Add(new MenuFlyoutItem { Text = "Sistema de Respaldos", Command = new Command(async () => await Run(Backup)) });
        archivos.Add(new MenuFlyoutItem { Text = "Cerrar Sesión", Command = new Command(async () => await SessionActions.SignOut(this, db)) });
        archivos.Add(new MenuFlyoutItem { Text = "Salir", Command = new Command(CloseApp) });
        var ayuda = new MenuBarItem { Text = "Ayuda" };
        ayuda.Add(new MenuFlyoutItem { Text = "Ayuda", Command = new Command(async () => await Help()) });
        var menu = ClassicWorkspaceChrome.Menu(this, [archivos, ayuda]);

        var tools = new HorizontalStackLayout { Spacing = 1, Padding = new Thickness(3, 2, 0, 0) };
        tools.Children.Add(ClassicWorkspaceChrome.Tool("Conectados", "gl_query.png", () => Run(ShowSessions)));
        tools.Children.Add(ClassicWorkspaceChrome.Tool("Respaldo", "gl_policy.png", () => Run(Backup)));
        tools.Children.Add(ClassicWorkspaceChrome.Tool("Salir", "gl_exit.png", () => { CloseApp(); return Task.CompletedTask; }));
        tools.Children.Add(ClassicWorkspaceChrome.SignOutTool(() => SessionActions.SignOut(this, db)));
        var toolbar = new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Background = ClassicWorkspaceChrome.ToolbarBrush(), Content = tools };

        companies.ItemTemplate = Template(nameof(Company.Name), null);
        companies.SelectionChanged += (_, e) => { if (e.CurrentSelection.FirstOrDefault() is Company c) userCompany.SelectedItem = companyList.FirstOrDefault(x => x.Id == c.Id); };
        users.ItemTemplate = Template(nameof(UserInfo.Username), nameof(UserInfo.CompanyName));
        users.SelectionChanged += (_, e) => SelectUser(e.CurrentSelection.FirstOrDefault() as UserInfo);

        var companyPanel = Panel("Contabilidades", companies, ["Contabilidad"],
            new VerticalStackLayout { Spacing = 6, Children = {
                Row("Nombre :", Box(companyName)),
                Buttons(JournalsPage.ActionButton("Agregar", "gl_add.png", () => Run(AddCompany), 90)) } });
        var userPanel = Panel("Usuarios", users, ["Usuario", "Contabilidad"],
            new VerticalStackLayout { Spacing = 6, Children = {
                Row("Usuario :", Box(username)), Row("Contabilidad :", userCompany),
                Row("Contraseña :", Box(password)), Row("Confirmar :", Box(confirmation)),
                Buttons(JournalsPage.ActionButton("Agregar", "gl_add.png", () => Run(AddUser), 90),
                    JournalsPage.ActionButton("Contraseña", "gl_change.png", () => Run(ChangePassword), 110),
                    JournalsPage.ActionButton("Borrar", "gl_delete.png", () => Run(DeleteUser), 90)) } });
        var panels = new Grid { ColumnSpacing = 12, Padding = new Thickness(12),
            ColumnDefinitions = { new ColumnDefinition(new GridLength(2, GridUnitType.Star)), new ColumnDefinition(new GridLength(3, GridUnitType.Star)) } };
        panels.Add(companyPanel, 0, 0); panels.Add(userPanel, 1, 0);
        status.Margin = new Thickness(12, 0); status.LineBreakMode = LineBreakMode.WordWrap;
        var body = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        body.Add(panels, 0, 0); body.Add(status, 0, 1);
        Content = ClassicWorkspaceChrome.Frame(menu, toolbar, body, ClassicWorkspaceChrome.Status(db, "Administración de empresas y usuarios"), "frame");
        Loaded += (_, _) => { if (Window != null) ClassicWorkspaceChrome.WindowTitle(Window, Title); };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (Window != null) ClassicWorkspaceChrome.WindowTitle(Window, Title);
        await Run(Load);
    }

    private async Task Load()
    {
        companyList = await db.Companies();
        companies.ItemsSource = companyList;
        userCompany.ItemsSource = companyList;
        if (userCompany.SelectedItem == null) userCompany.SelectedItem = companyList.FirstOrDefault();
        users.ItemsSource = await db.UserList();
    }

    private void SelectUser(UserInfo? user)
    {
        selectedUser = user;
        if (user == null) return;
        username.Text = user.Username;
        userCompany.SelectedItem = companyList.FirstOrDefault(c => c.Id == user.CompanyId);
        password.Text = confirmation.Text = "";
    }

    private async Task AddCompany()
    {
        await db.AddCompany(companyName.Text ?? "");
        status.Text = $"Contabilidad \"{companyName.Text?.Trim()}\" creada. Ahora agréguele un usuario para poder trabajarla.";
        companyName.Text = "";
        await Load();
    }

    private async Task AddUser()
    {
        if (userCompany.SelectedItem is not Company company) throw new InvalidOperationException("Seleccione la contabilidad del usuario.");
        if (password.Text != confirmation.Text) throw new InvalidOperationException("Las contraseñas no coinciden.");
        await db.AddUser(username.Text ?? "", password.Text ?? "", company.Id);
        status.Text = $"Usuario \"{username.Text?.Trim().ToLowerInvariant()}\" creado para {company.Name}. Con ese usuario se entra a esa contabilidad.";
        password.Text = confirmation.Text = ""; username.Text = "";
        await Load();
    }

    private async Task ChangePassword()
    {
        if (selectedUser == null) throw new InvalidOperationException("Seleccione un usuario de la lista.");
        if (password.Text != confirmation.Text) throw new InvalidOperationException("Las contraseñas no coinciden.");
        if (!await DisplayAlertAsync("Cambiar contraseña", $"¿Asignar la nueva contraseña a \"{selectedUser.Username}\"? Sus sesiones guardadas se cerrarán.", "Cambiar", "Cancelar")) return;
        await db.SetUserPassword(selectedUser.Id, password.Text ?? "");
        status.Text = $"Contraseña de \"{selectedUser.Username}\" cambiada.";
        password.Text = confirmation.Text = "";
    }

    private async Task DeleteUser()
    {
        if (selectedUser == null) throw new InvalidOperationException("Seleccione un usuario de la lista.");
        if (!await DisplayAlertAsync("Borrar usuario", $"¿Borrar al usuario \"{selectedUser.Username}\"? La contabilidad y sus datos se conservan.", "Borrar", "Cancelar")) return;
        await db.DeleteUser(selectedUser.Id);
        status.Text = $"Usuario \"{selectedUser.Username}\" borrado.";
        selectedUser = null; username.Text = "";
        await Load();
    }

    private async Task ShowSessions()
    {
        var sessions = await db.ActiveSessions();
        var text = sessions.Count == 0 ? "No hay sesiones abiertas." : string.Join("\n", sessions.Select(s =>
            $"{s.Username}{(s.IsMaster ? " (maestro)" : "")} · desde {s.CreatedAt:d/M/yyyy HH:mm} · último uso {s.LastUsedAt:d/M/yyyy HH:mm}"));
        await DisplayAlertAsync("Usuarios Conectados", text, "Cerrar");
    }

    private async Task Backup()
    {
        var folder = Path.Combine(FileSystem.AppDataDirectory, "Respaldos");
        if (!await DisplayAlertAsync("Sistema de Respaldos", $"Se hará una copia completa de la base de FREDI en:\n{folder}", "Respaldar", "Cancelar")) return;
        status.Text = "Respaldo creado: " + await db.Backup(folder);
    }

    private Task Help() => DisplayAlertAsync("Empresas y Usuarios",
        "El usuario maestro solo administra: crea contabilidades y los usuarios que las trabajan, cambia contraseñas y borra usuarios. " +
        "Cada contabilidad se abre entrando con su propio usuario (Cerrar Sesión y entrar con ese usuario).", "Cerrar");

    private void CloseApp()
    {
#if WINDOWS || MACCATALYST
        if (Window != null) Application.Current?.CloseWindow(Window);
#else
        Application.Current?.Quit();
#endif
    }

    private async Task Run(Func<Task> work)
    {
        status.TextColor = Colors.Black;
        try { await work(); }
        catch (InvalidOperationException ex) { status.TextColor = Gl2000Chrome.NegativeRed; status.Text = ex.Message; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); status.TextColor = Gl2000Chrome.NegativeRed; status.Text = "No se pudo completar la operación."; }
    }

    private static CollectionView List()
    {
        var list = new CollectionView { SelectionMode = SelectionMode.Single, BackgroundColor = Gl2000Chrome.RowYellow, HeightRequest = 260 };
        ClassicWorkspaceChrome.CompactRows(list);
        return list;
    }

    private static DataTemplate Template(string first, string? second) => new(() =>
    {
        var row = new Grid { HeightRequest = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(second == null ? 0 : 200) } };
        var a = ClassicWorkspaceChrome.Text("", true); a.Padding = new Thickness(4, 0); a.SetBinding(Label.TextProperty, first);
        row.Add(a, 0, 0);
        if (second != null) { var b = ClassicWorkspaceChrome.Text("", true); b.SetBinding(Label.TextProperty, second); row.Add(b, 1, 0); }
        return row;
    });

    private static View Panel(string title, CollectionView list, string[] headers, View editor)
    {
        var header = new Grid { HeightRequest = 17, Background = ClassicWorkspaceChrome.ToolbarBrush(),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(headers.Length > 1 ? 200 : 0) } };
        for (var i = 0; i < headers.Length; i++) header.Add(ClassicWorkspaceChrome.Text((i == 0 ? " " : "") + headers[i], true), i, 0);
        var caption = ClassicWorkspaceChrome.Text(title, true); caption.FontSize = 12;
        return new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 8, BackgroundColor = Gl2000Chrome.HeaderBlue, VerticalOptions = LayoutOptions.Start,
            Content = new VerticalStackLayout { Spacing = 8, Children = { caption,
                new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Content = new VerticalStackLayout { Children = { header, list } } }, editor } } };
    }

    private static View Row(string label, View field)
    {
        var grid = new Grid { ColumnSpacing = 6, ColumnDefinitions = { new ColumnDefinition(100), new ColumnDefinition(GridLength.Star) } };
        var text = ClassicWorkspaceChrome.Text(label, true); text.TextColor = Color.FromArgb("#0000FF");
        grid.Add(text, 0, 0); grid.Add(field, 1, 0);
        return grid;
    }

    private static View Buttons(params View[] buttons)
    {
        var row = new HorizontalStackLayout { Spacing = 5, HorizontalOptions = LayoutOptions.End };
        foreach (var b in buttons) row.Children.Add(b);
        return row;
    }

    private static Entry Field(string name, int maxLength, bool secret = false)
    {
        var entry = new Entry { MaxLength = maxLength, IsPassword = secret, FontFamily = "Arial", FontSize = 11, TextColor = Colors.Black,
            BackgroundColor = Colors.White, HeightRequest = 20, IsTextPredictionEnabled = false, IsSpellCheckEnabled = false };
        SemanticProperties.SetDescription(entry, name);
        ClassicWorkspaceChrome.CompactEntry(entry);
        return entry;
    }

    private static Border Box(Entry entry) => new() { Content = entry, Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, BackgroundColor = Colors.White };
}
