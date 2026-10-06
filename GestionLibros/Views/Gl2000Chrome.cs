using GestionLibros.Data;
using GestionLibros.Models;

namespace GestionLibros.Views;

// Reproduces the persistent chrome of the old GL2000 screens: top menu, icon toolbar
// with the current period, and bottom status bar. Shared by every AccountingPage so
// sub-windows look like the original MDI children instead of isolated pages.
internal static class Gl2000Chrome
{
    internal static readonly Color ToolbarBg = Color.FromArgb("#ECE9D8");
    internal static readonly Color StatusBarBg = Color.FromArgb("#ECE9D8");
    internal static readonly Color HeaderBlue = Color.FromArgb("#A6CAF1");
    internal static readonly Color RowYellow = Color.FromArgb("#F8F7C0");
    internal static readonly Color SelectedNavy = Color.FromArgb("#000080");
    internal static readonly Color LinkBlue = Color.FromArgb("#0000AA");
    internal static readonly Color NegativeRed = Color.FromArgb("#CC0000");
    internal static readonly Color WorkspaceGray = Color.FromArgb("#808080");

    // The real GL2000 wallpaper bitmaps (converted to PNG, bundled under Resources/Images), tiled the
    // same way estilo.ini used them (WallPaperType=2 = tile). card.png = Process/Report windows,
    // wood.png = data-entry Form windows, gl_fusion5.png = the main Frame watermark, gl_graymarb.png = Splash,
    // gl_bgmarble.png = About.
    internal static View Tiled(string file, int tileWidth, int tileHeight, int columns = 16, int rows = 14)
    {
        // Fixed coordinates avoid FlexLayout shrinking tiles or leaving partially empty rows.
        var tiles = new AbsoluteLayout { IsClippedToBounds = true, InputTransparent = true };
        var lastColumns = 0;
        var lastRows = 0;
        tiles.SizeChanged += (_, _) =>
        {
            if (tiles.Width <= 0 || tiles.Height <= 0) return;
            var across = (int)Math.Ceiling(tiles.Width / tileWidth);
            var down = (int)Math.Ceiling(tiles.Height / tileHeight);
            if (across == lastColumns && down == lastRows) return;
            lastColumns = across; lastRows = down;
            tiles.Children.Clear();
            for (var row = 0; row < down; row++)
                for (var column = 0; column < across; column++)
                {
                    var image = new Image { Source = file, Aspect = Aspect.Fill };
                    AbsoluteLayout.SetLayoutBounds(image, new Rect(column * tileWidth, row * tileHeight, tileWidth, tileHeight));
                    tiles.Children.Add(image);
                }
        };
        return tiles;
    }

    // Layers a tiled texture behind page content, like the wallpaper the original templates painted
    // behind each window type. Rows/fields in front are opaque, so the texture only shows through gaps.
    internal static View Backdrop(View content, string file, int tileWidth, int tileHeight)
    {
        var grid = new Grid { IsClippedToBounds = true };
        grid.Add(Tiled(file, tileWidth, tileHeight));
        grid.Add(content);
        return grid;
    }

    // Matches each wallpaper to the estilo.ini window category it belonged to (file, native tile size).
    internal static (string file, int w, int h) Texture(string key) => key switch
    {
        "form" => ("gl_wood.png", 59, 75),
        "splash" => ("gl_graymarb.png", 150, 170),
        "about" => ("gl_bgmarble.png", 110, 130),
        "frame" => ("gl_fusion5.png", 234, 127),
        _ => ("gl_card.png", 33, 31),
    };

    internal static Task Navigate(Page from, AppDatabase db, Company company, int year, int month, string key) =>
        from.Navigation.PushAsync(key switch
        {
            "catalog" => new CatalogPage(db, company),
            "journals" => new JournalsPage(db, company, year, month),
            "trial" => new TrialBalancePage(db, company, year, month),
            "ledger" => new LedgerPage(db, company, year, month),
            "admin" => new AdminPage(db),
            _ => new BalancesPage(db, company, year, month),
        });

    internal static View IconButton(string label, string glyph, Func<Task> action)
    {
        var icon = new Label { Text = glyph, FontSize = 22, HorizontalTextAlignment = TextAlignment.Center };
        var text = new Label { Text = label, FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Colors.Black, HorizontalTextAlignment = TextAlignment.Center };
        var stack = new VerticalStackLayout { Spacing = 2, Children = { icon, text }, WidthRequest = 64 };
        var border = new Border { Content = stack, Padding = new Thickness(4, 6), Stroke = Colors.Transparent, BackgroundColor = Colors.Transparent };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await action();
        border.GestureRecognizers.Add(tap);
        return border;
    }

    // The 8 standard icons from the main toolbar (Catálogo, Asientos, Acumular, Consulta, Balanza, Saldos, Mayor, Salir).
    internal static IEnumerable<View> StandardIcons(Page page, Func<string, Task> open, Func<Task> exit) => new[]
    {
        IconButton("Catálogo", "📘", () => open("catalog")),
        IconButton("Asientos", "🗒️", () => open("journals")),
        IconButton("Acumular", "🚦", () => page.DisplayAlertAsync("Aviso", "\"Acumular Saldos\" no está disponible en esta versión de prueba.", "Entendido")),
        IconButton("Consulta", "🔎", () => open("balances")),
        IconButton("Balanza", "⚖️", () => open("trial")),
        IconButton("Saldos", "🧮", () => open("balances")),
        IconButton("Mayor", "📒", () => open("ledger")),
        IconButton("Salir", "✖", exit),
    };

    private static View PeriodBadge(string label, string value)
    {
        var box = new Border
        {
            Content = new Label { Text = value, Padding = new Thickness(6, 2), FontAttributes = FontAttributes.Bold },
            BackgroundColor = Colors.White,
            Stroke = Colors.Gray,
            StrokeThickness = 1,
            Padding = 0,
        };
        return new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center, Children = { new Label { Text = label, VerticalOptions = LayoutOptions.Center }, box } };
    }

    // Fixed-period toolbar used by sub-windows whose company/year/month never change (catalog, journals, reports, forms).
    internal static View Toolbar(Page page, AppDatabase db, Company company, int year, int month, Func<Task>? onExit = null)
    {
        Task Open(string key) => Navigate(page, db, company, year, month, key);
        var icons = new HorizontalStackLayout { Spacing = 2, Padding = new Thickness(6, 4) };
        foreach (var icon in StandardIcons(page, Open, onExit ?? (() => page.Navigation.PopAsync()))) icons.Children.Add(icon);

        var period = new HorizontalStackLayout { Spacing = 14, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(12, 0) };
        period.Children.Add(PeriodBadge("Mes", month.ToString("00")));
        period.Children.Add(PeriodBadge("Año", year.ToString()));

        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, BackgroundColor = ToolbarBg };
        grid.Add(icons, 0, 0);
        grid.Add(period, 1, 0);
        return new VerticalStackLayout { Children = { grid, new BoxView { HeightRequest = 1, BackgroundColor = Colors.Gray } } };
    }

    internal static View StatusBar(AppDatabase db, string hint = "")
    {
        var left = new Label { Text = hint, FontSize = 11, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
        var level = new Label { Text = $"Nivel : {(db.Session?.IsMaster == true ? "Maestro" : "Operador")}", FontSize = 11, VerticalOptions = LayoutOptions.Center };
        var user = new Label { Text = $"Usuario : {db.Session?.Username}", FontSize = 11, VerticalOptions = LayoutOptions.Center };
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 18,
            Padding = new Thickness(8, 3),
            BackgroundColor = StatusBarBg,
        };
        grid.Add(left, 0, 0); grid.Add(level, 1, 0); grid.Add(user, 2, 0);
        return new VerticalStackLayout { Children = { new BoxView { HeightRequest = 1, BackgroundColor = Colors.Gray }, grid } };
    }

    // Reproduces the full Archivos/Editar/Operación/Reportes/Utilerías/Ventana/Ayuda menu tree from the
    // original screens. Items without an equivalent yet show a notice instead of silently doing nothing.
    internal static void ApplyMenus(ContentPage page, Func<string, Task>? open)
    {
        page.MenuBarItems.Clear();
        Task Stub(string name) => page.DisplayAlertAsync("Aviso", $"\"{name}\" no está disponible en esta versión de prueba.", "Entendido");
        Task Go(string key, string name) => open != null ? open(key) : Stub(name);

        var archivos = new MenuBarItem { Text = "Archivos" };
        void A(string text, Func<Task>? action = null) => archivos.Add(new MenuFlyoutItem { Text = text, Command = new Command(async () => await (action ?? (() => Stub(text)))()) });
        A("Tipos de Pólizas");
        A("Centros de Costo");
        A("Catálogo de Cuentas", () => Go("catalog", "Catálogo de Cuentas"));
        A("Asientos Contables", () => Go("journals", "Asientos Contables"));
        A("Maestro de Bancos");
        A("Maestro de Departamentos");
        A("Maestro de Conceptos");
        A("Tipos de Transacciones");
        A("Transacciones de Bancos");
        var conciliacion = new MenuFlyoutSubItem { Text = "Conciliación Bancaria" };
        conciliacion.Add(new MenuFlyoutItem { Text = "Cheques en Tránsito", Command = new Command(async () => await Stub("Conciliación Bancaria · Cheques en Tránsito")) });
        conciliacion.Add(new MenuFlyoutItem { Text = "Reporte de Conciliación", Command = new Command(async () => await Stub("Conciliación Bancaria · Reporte")) });
        archivos.Add(conciliacion);
        var seguridad = new MenuFlyoutSubItem { Text = "Seguridad" };
        seguridad.Add(new MenuFlyoutItem { Text = "Usuarios", Command = new Command(async () => await Go("admin", "Empresas y usuarios")) });
        seguridad.Add(new MenuFlyoutItem { Text = "Usuarios Conectados", Command = new Command(async () => await Stub("Usuarios Conectados")) });
        archivos.Add(seguridad);
        var estilos = new MenuFlyoutSubItem { Text = "Estilos" };
        estilos.Add(new MenuFlyoutItem { Text = "Configurar Estilos", Command = new Command(async () => await Stub("Estilos")) });
        archivos.Add(estilos);
        A("Mantenimiento de Archivos");
        A("Empresas", () => Go("admin", "Empresas"));
        A("Parámetros ...");
        A("Configurar Impresora ...");
        A("Salir", () => Stub("Salir"));
        page.MenuBarItems.Add(archivos);

        var editar = new MenuBarItem { Text = "Editar" };
        editar.Add(new MenuFlyoutItem { Text = "Cortar", IsEnabled = false });
        editar.Add(new MenuFlyoutItem { Text = "Copiar", IsEnabled = false });
        editar.Add(new MenuFlyoutItem { Text = "Pegar", IsEnabled = false });
        page.MenuBarItems.Add(editar);

        var operacion = new MenuBarItem { Text = "Operación" };
        void O(string text, Func<Task>? action = null) => operacion.Add(new MenuFlyoutItem { Text = text, Command = new Command(async () => await (action ?? (() => Stub(text)))()) });
        O("Captura de Cheques");
        O("Consulta del Catálogo", () => Go("catalog", "Consulta del Catálogo"));
        O("Consulta de Bancos");
        O("Acumular Saldos");
        O("Cierre Anual");
        O("Impresión de Cheques");
        O("Impresión de Pólizas");
        O("Cancelación de Cheques");
        O("Desgloce de Movimientos");
        O("Conciliar Proveedores / Clientes");
        page.MenuBarItems.Add(operacion);

        var reportes = new MenuBarItem { Text = "Reportes" };
        reportes.Add(new MenuFlyoutItem { Text = "Balanza de comprobación", Command = new Command(async () => await Go("trial", "Balanza de comprobación")) });
        reportes.Add(new MenuFlyoutItem { Text = "Mayor de cuenta", Command = new Command(async () => await Go("ledger", "Mayor de cuenta")) });
        reportes.Add(new MenuFlyoutItem { Text = "Balance General", Command = new Command(async () => await Stub("Balance General")) });
        reportes.Add(new MenuFlyoutItem { Text = "Estado de Resultados", Command = new Command(async () => await Stub("Estado de Resultados")) });
        reportes.Add(new MenuFlyoutItem { Text = "Comparativo Anual", Command = new Command(async () => await Stub("Comparativo Anual")) });
        page.MenuBarItems.Add(reportes);

        var utilerias = new MenuBarItem { Text = "Utilerías" };
        void U(string text) => utilerias.Add(new MenuFlyoutItem { Text = text, Command = new Command(async () => await Stub(text)) });
        U("Pólizas sin Movimientos");
        U("Renumerar Pólizas de Egresos (GZZ)");
        U("Arreglar Fecha en Partidas");
        U("Copia de Catálogos");
        U("Traspasar Movimientos");
        U("Concentrar Movimientos");
        U("Borrar Saldos Anteriores");
        U("Exportar Saldos Iniciales");
        U("Importar Saldos Iniciales");
        var recuperacion = new MenuFlyoutSubItem { Text = "Recuperación de Datos" };
        recuperacion.Add(new MenuFlyoutItem { Text = "Recuperar Todo", Command = new Command(async () => await Stub("Recuperación de Datos")) });
        utilerias.Add(recuperacion);
        U("Importar / Exportar Catálogo CSV");
        U("Editar Archivo INI");
        U("Generar Archivo INI");
        U("Bitácora de Mensajes");
        U("Sistema de Respaldos");
        page.MenuBarItems.Add(utilerias);

        var ventana = new MenuBarItem { Text = "Ventana" };
        ventana.Add(new MenuFlyoutItem { Text = "Cascada", Command = new Command(async () => await Stub("Ventana · Cascada")) });
        page.MenuBarItems.Add(ventana);

        var ayuda = new MenuBarItem { Text = "Ayuda" };
        ayuda.Add(new MenuFlyoutItem { Text = "Acerca de FREDI", Command = new Command(async () => await page.Navigation.PushAsync(new AboutPage())) });
        page.MenuBarItems.Add(ayuda);
    }
}
