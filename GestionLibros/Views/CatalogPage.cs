using System.ComponentModel;
using GestionLibros.Data;
using GestionLibros.Models;

namespace GestionLibros.Views;

public class CatalogPage : AccountingPage
{
    private readonly AppDatabase db;
    private readonly Company company;
    private readonly int year;
    private readonly int month;
    private int activeTab;
    private string filter = "";
    private List<Account> accounts = [];
    private List<CatalogRow> visibleRows = [];
    private CatalogRow? selected;
    private readonly CollectionView table = new() { SelectionMode = SelectionMode.Single, BackgroundColor = Colors.Transparent };
    private readonly Entry locator = new() { HeightRequest = 20, MinimumHeightRequest = 0, FontFamily = "Arial", FontSize = 11,
        FontAutoScalingEnabled = false, TextColor = Colors.Black, BackgroundColor = Colors.White, Margin = 0 };
    private readonly List<Button> tabs = [];
    private readonly AccountDialog dialog;
    private readonly Label count = ClassicWorkspaceChrome.Text("");
    private static readonly string[] Headers = ["Cuenta", "CtaSup", "Nivel", "Descripción", "Fecha Cambio", "Usuario", "Status"];
    private static readonly int[] Widths = [62, 62, 41, 300, 87, 87, 40];
    private static readonly string[] TabNames = ["Numero", "Cta. Superior", "Descripción"];

    public CatalogPage(AppDatabase db, Company company, int year, int month) : base("Catálogo de Cuentas")
    {
        this.db = db; this.company = company; this.year = year; this.month = month;
        dialog = new AccountDialog(this, db, company, SelectSaved);
        NavigationPage.SetHasNavigationBar(this, false);
        Title = $"Sistema de Contabilidad (GL 2000) - {company.Name} - [Catálogo de Cuentas]";
        Gl2000Chrome.ApplyMenus(this, Open);
        MenuBarItems[0].OfType<MenuFlyoutItem>().First(x => x.Text == "Salir").Command = new Command(async () => await Navigation.PopAsync());
        var menu = ClassicWorkspaceChrome.Menu(this, MenuBarItems.ToArray());
        MenuBarItems.Clear();

        // Sort tabs sit on the wallpaper, left-aligned, like the original index tabs.
        var tabRow = new HorizontalStackLayout { Spacing = 1, HeightRequest = 20 };
        for (var i = 0; i < TabNames.Length; i++)
        {
            var index = i;
            var button = JournalsPage.FlatButton(TabNames[i], () => { activeTab = index; Render(selected?.Account.Id); return Task.CompletedTask; });
            button.HeightRequest = 20; button.Padding = new Thickness(4, 0); button.FontAttributes = FontAttributes.Bold;
            tabRow.Children.Add(button); tabs.Add(button);
        }

        var locatorRow = new HorizontalStackLayout { Spacing = 6, Padding = new Thickness(6, 4) };
        locatorRow.Children.Add(ClassicWorkspaceChrome.Text("Localizador :", true));
        locatorRow.Children.Add(new Border { Content = locator, WidthRequest = 220, Padding = new Thickness(1, 0), Stroke = Colors.Gray, StrokeThickness = 1, BackgroundColor = Colors.White });
        SemanticProperties.SetDescription(locator, "Localizador");
        ClassicWorkspaceChrome.CompactEntry(locator);
        locator.TextChanged += (_, _) => Locate();
        locator.Completed += (_, _) => Locate();

        var gridHeader = Columns();
        gridHeader.HeightRequest = 17;
        for (var i = 0; i < Headers.Length; i++)
        {
            var label = ClassicWorkspaceChrome.Text(Headers[i], true);
            label.HorizontalTextAlignment = TextAlignment.Center;
            gridHeader.Add(new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0,
                Background = ClassicWorkspaceChrome.ToolbarBrush(), Content = label }, i, 0);
        }
        table.ItemTemplate = new DataTemplate(() =>
        {
            var row = Columns(); row.HeightRequest = 14;
            row.SetBinding(VisualElement.BackgroundColorProperty, nameof(CatalogRow.Background));
            for (var i = 0; i < Headers.Length; i++)
            {
                var label = ClassicWorkspaceChrome.Text("", true);
                label.Padding = new Thickness(3, 0);
                label.LineBreakMode = LineBreakMode.NoWrap;
                label.SetBinding(Label.TextProperty, $"Cells[{i}]");
                label.SetBinding(Label.TextColorProperty, nameof(CatalogRow.Foreground));
                if (i is 2 or 6) label.HorizontalTextAlignment = TextAlignment.Center;
                if (i == 4) label.HorizontalTextAlignment = TextAlignment.End;
                row.Add(label, i, 0);
            }
            var doubleClick = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
            doubleClick.Tapped += async (_, _) =>
            {
                if (row.BindingContext is CatalogRow item) { Select(item); await Guard(Edit); }
            };
            row.GestureRecognizers.Add(doubleClick);
            return row;
        });
        table.SelectionChanged += (_, e) => Select(e.CurrentSelection.FirstOrDefault() as CatalogRow);
        ClassicWorkspaceChrome.CompactRows(table);
        var minimumWidth = Widths.Sum() + 20;
        var data = new Grid { RowSpacing = 0, RowDefinitions = { new RowDefinition(17), new RowDefinition(GridLength.Star) }, MinimumWidthRequest = minimumWidth };
        var ruledArea = Columns(); ruledArea.BackgroundColor = Gl2000Chrome.RowYellow;
        for (var i = 0; i < Widths.Length; i++)
            ruledArea.Add(new BoxView { WidthRequest = 1, Color = Color.FromArgb("#909078"), HorizontalOptions = LayoutOptions.End, InputTransparent = true }, i, 0);
        ruledArea.Add(table); Grid.SetColumnSpan(table, Widths.Length + 1);
        data.Add(gridHeader, 0, 0); data.Add(ruledArea, 0, 1);
        var scroll = new ScrollView { Margin = new Thickness(7, 0), Orientation = ScrollOrientation.Horizontal, Content = data, HorizontalScrollBarVisibility = ScrollBarVisibility.Always };
        scroll.SizeChanged += (_, _) => { if (scroll.Width > 0) data.WidthRequest = Math.Max(minimumWidth, scroll.Width); };

        var navigation = new HorizontalStackLayout { Spacing = 1, HeightRequest = 18, Margin = new Thickness(7, 0), BackgroundColor = Color.FromArgb("#F0F0F0") };
        foreach (var (caption, step) in new[] { ("|◀", int.MinValue), ("◀◀", -PageSize), ("◀", -1), ("?", 0), ("▶", 1), ("▶▶", PageSize), ("▶|", int.MaxValue) })
        {
            var nav = JournalsPage.FlatButton(caption, () => { if (step == 0) locator.Focus(); else Move(step); return Task.CompletedTask; }, caption.Length > 1 ? 22 : 19);
            nav.BackgroundColor = Colors.LightGray; navigation.Children.Add(nav);
        }
        count.Margin = new Thickness(8, 0); navigation.Children.Add(count);

        var edits = new HorizontalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.End, Padding = new Thickness(0, 4, 8, 4) };
        edits.Children.Add(JournalsPage.ActionButton("Agregar", "gl_add.png", () => { dialog.Show(null, accounts); return Task.CompletedTask; }));
        edits.Children.Add(JournalsPage.ActionButton("Cambiar", "gl_change.png", () => Guard(Edit)));
        edits.Children.Add(JournalsPage.ActionButton("Borrar", "gl_delete.png", () => Guard(Delete)));

        var panel = new Grid { Padding = 1, BackgroundColor = Gl2000Chrome.HeaderBlue, RowSpacing = 0,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(18), new RowDefinition(36) } };
        Notice.FontSize = 11; Notice.Padding = new Thickness(6, 0);
        panel.Add(locatorRow, 0, 0); panel.Add(Notice, 0, 1); panel.Add(scroll, 0, 2); panel.Add(navigation, 0, 3); panel.Add(edits, 0, 4);

        var footer = new Grid { Padding = new Thickness(8, 1), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        var left = new HorizontalStackLayout { Spacing = 5 };
        left.Children.Add(JournalsPage.ActionButton("Filtro", "gl_filter.png", Filter, 82));
        left.Children.Add(JournalsPage.ActionButton("C. Costo", "gl_catalog.png", () => DisplayAlertAsync("Centros de Costo", "Los centros de costo todavía no están implementados.", "Cerrar"), 82));
        footer.Add(left, 0, 0);
        var right = new HorizontalStackLayout { Spacing = 5 };
        right.Children.Add(JournalsPage.ActionButton("Salir", "gl_close.png", async () => await Navigation.PopAsync(), 82));
        right.Children.Add(JournalsPage.ActionButton("Ayuda", "gl_help.png", Help, 82));
        footer.Add(right, 1, 0);

        var body = new Grid { Padding = new Thickness(8, 6, 8, 0), RowSpacing = 0,
            RowDefinitions = { new RowDefinition(20), new RowDefinition(GridLength.Star), new RowDefinition(30) } };
        body.Add(tabRow, 0, 0); body.Add(panel, 0, 1); body.Add(footer, 0, 2);
        var frame = ClassicWorkspaceChrome.Frame(menu, ClassicWorkspaceChrome.ModuleToolbar(Open, year, month), body,
            ClassicWorkspaceChrome.Status(db, "Recorriendo Registros"));
        Content = new Grid { Children = { frame, dialog } };
        Loaded += (_, _) => { if (Window != null) ClassicWorkspaceChrome.WindowTitle(Window, Title); };
    }

    private const int PageSize = 20;

    private static Grid Columns()
    {
        var grid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };
        foreach (var width in Widths) grid.ColumnDefinitions.Add(new ColumnDefinition(width));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); return grid;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (Window != null) ClassicWorkspaceChrome.WindowTitle(Window, Title);
        await Guard(Load);
    }

    internal Task Reload() => Guard(Load);

    private async Task Load()
    {
        var keep = selected?.Account.Id;
        accounts = await db.Accounts(company.Id);
        Render(keep);
    }

    private string SortKey(Account account) => activeTab switch
    {
        1 => account.ParentCode,
        2 => account.Description,
        _ => account.Code,
    };

    private void Render(int? keep = null)
    {
        var parents = accounts.Select(a => a.ParentCode).ToHashSet();
        IEnumerable<Account> result = accounts;
        if (filter.Length > 0)
            result = result.Where(a => $"{a.Code} {a.ParentCode} {a.Description} {a.ChangedBy}".Contains(filter, StringComparison.CurrentCultureIgnoreCase));
        result = result.OrderBy(SortKey, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.Code, StringComparer.Ordinal);
        selected = null;
        visibleRows = result.Select(a => new CatalogRow(a, !parents.Contains(a.Code))).ToList();
        table.ItemsSource = visibleRows;
        var target = visibleRows.FirstOrDefault(r => r.Account.Id == keep) ?? visibleRows.FirstOrDefault();
        table.SelectedItem = target;
        if (target != null) table.ScrollTo(target, position: ScrollToPosition.MakeVisible, animate: false);
        for (var i = 0; i < tabs.Count; i++) tabs[i].BackgroundColor = i == activeTab ? Gl2000Chrome.HeaderBlue : Color.FromArgb("#A5A7E5");
        count.Text = $"{visibleRows.Count} cuentas";
        Notice.Text = filter.Length > 0 ? $"Filtro: {filter}" : "";
    }

    private void Select(CatalogRow? item)
    {
        selected?.SetSelected(false); selected = item; selected?.SetSelected(true);
    }

    private void Move(int step)
    {
        if (visibleRows.Count == 0) return;
        var current = selected == null ? 0 : visibleRows.IndexOf(selected);
        var index = step == int.MinValue ? 0 : step == int.MaxValue ? visibleRows.Count - 1 : Math.Clamp(current + step, 0, visibleRows.Count - 1);
        table.SelectedItem = visibleRows[index]; table.ScrollTo(visibleRows[index], position: ScrollToPosition.MakeVisible, animate: false);
    }

    // Like the original locator: jumps to the first row whose active-tab key starts with the text; it does not filter.
    private void Locate()
    {
        var text = locator.Text?.Trim() ?? "";
        if (text.Length == 0) return;
        var item = visibleRows.FirstOrDefault(r => SortKey(r.Account).StartsWith(text, StringComparison.CurrentCultureIgnoreCase));
        if (item == null) return;
        table.SelectedItem = item; table.ScrollTo(item, position: ScrollToPosition.Start, animate: false);
    }

    private Task Edit()
    {
        if (selected == null) throw new InvalidOperationException("Seleccione una cuenta.");
        dialog.Show(selected.Account, accounts);
        return Task.CompletedTask;
    }

    private async Task SelectSaved(string code)
    {
        await Load();
        var row = visibleRows.FirstOrDefault(r => r.Account.Code == code);
        if (row == null) return;
        table.SelectedItem = row; table.ScrollTo(row, position: ScrollToPosition.MakeVisible, animate: false);
    }

    private async Task Delete()
    {
        if (selected == null) throw new InvalidOperationException("Seleccione una cuenta.");
        var account = selected.Account;
        if (await DisplayAlertAsync("Borrar cuenta", $"¿Borrar {account.Code} — {account.Description}?", "Borrar", "Cancelar"))
        { await db.DeleteAccount(company.Id, account.Id); await Load(); }
    }

    private Task Filter() => Guard(async () =>
    {
        var text = await DisplayPromptAsync("Filtro", "Cuenta, cuenta superior, descripción o usuario. Deje vacío para mostrar todas.", "Aplicar", "Cancelar", initialValue: filter);
        if (text == null) return; filter = text.Trim(); Render(selected?.Account.Id);
    });

    private Task Help() => DisplayAlertAsync("Catálogo de Cuentas", "Las pestañas ordenan por número, cuenta superior o descripción. El Localizador salta a la primera cuenta que empieza con el texto según la pestaña activa. Filtro muestra solo las cuentas que contienen el texto. Un * junto al nivel indica una cuenta de detalle (sin subcuentas), la única que acepta movimientos. Doble clic o Cambiar modifica la descripción.", "Cerrar");

    private Task Open(string key) => Guard(async () =>
    {
        if (key == "exit") { await Navigation.PopAsync(); return; }
        if (key == "catalog") { await Load(); return; }
        if (key == "accumulate") { await MenuActions.Accumulate(this); return; }
        await Gl2000Chrome.Navigate(this, db, company, year, month, key);
    });

    private sealed class CatalogRow(Account account, bool detail) : INotifyPropertyChanged
    {
        public Account Account { get; } = account;
        public string[] Cells { get; } = [account.Code, account.ParentCode, detail ? $"{account.Level}*" : account.Level.ToString(),
            account.Description, account.ChangedAt == default ? "" : account.ChangedAt.ToString("d/M/yyyy"), account.ChangedBy ?? "", account.Status ?? ""];
        public Color Background { get; private set; } = Gl2000Chrome.RowYellow;
        public Color Foreground { get; private set; } = Colors.Black;
        public event PropertyChangedEventHandler? PropertyChanged;
        public void SetSelected(bool value)
        {
            Background = value ? Color.FromArgb("#0078D7") : Gl2000Chrome.RowYellow;
            Foreground = value ? Colors.White : Colors.Black;
            PropertyChanged?.Invoke(this, new(nameof(Background))); PropertyChanged?.Invoke(this, new(nameof(Foreground)));
        }
    }
}
