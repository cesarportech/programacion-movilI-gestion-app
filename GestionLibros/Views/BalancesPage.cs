using System.Globalization;
using GestionLibros.Data;
using GestionLibros.Models;
using GestionLibros.Reporting;

namespace GestionLibros.Views;

public class BalancesPage : AccountingPage
{
    private readonly AppDatabase db;
    private readonly Company company;
    private readonly int year;
    private int month;
    private int activeTab;
    private string filter = "";
    private List<BalanceRow> balances = [];
    private List<BalanceDisplayRow> visibleRows = [];
    private BalanceDisplayRow? selected;
    private readonly CollectionView table = new() { SelectionMode = SelectionMode.Single, BackgroundColor = Colors.Transparent };
    private readonly Entry locator = new() { HeightRequest = 20, MinimumHeightRequest = 0, FontFamily = "Arial", FontSize = 11,
        FontAutoScalingEnabled = false, TextColor = Colors.Black, BackgroundColor = Colors.White, Margin = 0 };
    private readonly Picker monthPicker = new() { WidthRequest = 51, HeightRequest = 20, MinimumHeightRequest = 0,
        FontFamily = "Arial", FontSize = 11, TextColor = Colors.Black, BackgroundColor = Colors.White };
    private readonly List<Button> tabs = [];
    private readonly Label count = ClassicWorkspaceChrome.Text("");
    private static readonly string[] Headers = ["Cuenta", "Descripción", "Saldo Ini", "Cargos", "Creditos", "Saldo Actual"];
    private static readonly int[] Widths = [58, 210, 92, 92, 92, 92];
    private static readonly Color DetailBlue = Color.FromArgb("#0000C0");
    private const int PageSize = 20;

    public BalancesPage(AppDatabase db, Company company, int year, int month) : base("Consulta de Cuentas")
    {
        this.db = db; this.company = company; this.year = year; this.month = month;
        NavigationPage.SetHasNavigationBar(this, false);
        UpdateTitle();
        Gl2000Chrome.ApplyMenus(this, Open);
        MenuBarItems[0].OfType<MenuFlyoutItem>().First(x => x.Text == "Salir").Command = new Command(async () => await Navigation.PopAsync());
        var menu = ClassicWorkspaceChrome.Menu(this, MenuBarItems.ToArray());
        MenuBarItems.Clear();

        var tabRow = new HorizontalStackLayout { Spacing = 1, HeightRequest = 20 };
        foreach (var (name, index) in new[] { ("Cuenta", 0), ("Nombre", 1) })
        {
            var button = JournalsPage.FlatButton(name, () => { activeTab = index; Render(selected?.Row.Code); return Task.CompletedTask; });
            button.HeightRequest = 20; button.Padding = new Thickness(4, 0); button.FontAttributes = FontAttributes.Bold;
            tabRow.Children.Add(button); tabs.Add(button);
        }

        var locatorRow = new Grid { Padding = new Thickness(6, 3), ColumnSpacing = 6,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(220), new ColumnDefinition(GridLength.Star) } };
        locatorRow.Add(ClassicWorkspaceChrome.Text("Localizador :", true), 0, 0);
        locatorRow.Add(new Border { Content = locator, Padding = new Thickness(1, 0), Stroke = Colors.Gray, StrokeThickness = 1, BackgroundColor = Colors.White }, 1, 0);
        var legend = ClassicWorkspaceChrome.Text("Cuentas de Detalle", true);
        legend.TextColor = Color.FromArgb("#D8F6FF"); legend.HorizontalTextAlignment = TextAlignment.Center;
        locatorRow.Add(legend, 2, 0);
        SemanticProperties.SetDescription(locator, "Localizador");
        locator.TextChanged += (_, _) => Locate();
        locator.Completed += (_, _) => Locate();

        var gridHeader = Columns(); gridHeader.HeightRequest = 17;
        for (var i = 0; i < Headers.Length; i++)
        {
            var label = ClassicWorkspaceChrome.Text(Headers[i], true); label.HorizontalTextAlignment = TextAlignment.Center;
            gridHeader.Add(new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Background = ClassicWorkspaceChrome.ToolbarBrush(), Content = label }, i, 0);
        }
        table.ItemTemplate = new DataTemplate(() =>
        {
            var row = Columns(); row.HeightRequest = 14;
            row.SetBinding(VisualElement.BackgroundColorProperty, nameof(BalanceDisplayRow.Background));
            for (var i = 0; i < Headers.Length; i++)
            {
                var label = ClassicWorkspaceChrome.Text("", true);
                label.Padding = new Thickness(3, 0); label.LineBreakMode = LineBreakMode.NoWrap;
                label.SetBinding(Label.TextProperty, $"Cells[{i}]");
                label.SetBinding(Label.TextColorProperty, $"Colors[{i}]");
                if (i >= 2) label.HorizontalTextAlignment = TextAlignment.End;
                row.Add(label, i, 0);
            }
            var doubleClick = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
            doubleClick.Tapped += async (_, _) => { if (row.BindingContext is BalanceDisplayRow item) { Select(item); await Guard(OpenLedger); } };
            row.GestureRecognizers.Add(doubleClick);
            return row;
        });
        table.SelectionChanged += (_, e) => Select(e.CurrentSelection.FirstOrDefault() as BalanceDisplayRow);
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

        var navigation = new HorizontalStackLayout { Spacing = 1, HeightRequest = 18, Margin = new Thickness(7, 0, 7, 4), BackgroundColor = Color.FromArgb("#F0F0F0") };
        foreach (var (caption, step) in new[] { ("|◀", int.MinValue), ("◀◀", -PageSize), ("◀", -1), ("?", 0), ("▶", 1), ("▶▶", PageSize), ("▶|", int.MaxValue) })
        {
            var nav = JournalsPage.FlatButton(caption, () => { if (step == 0) locator.Focus(); else Move(step); return Task.CompletedTask; }, caption.Length > 1 ? 22 : 19);
            nav.BackgroundColor = Colors.LightGray; navigation.Children.Add(nav);
        }
        count.Margin = new Thickness(8, 0); navigation.Children.Add(count);

        var panel = new Grid { Padding = 1, BackgroundColor = Gl2000Chrome.HeaderBlue, RowSpacing = 0,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(22) } };
        Notice.FontSize = 11; Notice.Padding = new Thickness(6, 0);
        panel.Add(locatorRow, 0, 0); panel.Add(Notice, 0, 1); panel.Add(scroll, 0, 2); panel.Add(navigation, 0, 3);

        monthPicker.ItemsSource = Enumerable.Range(1, 12).Select(m => m.ToString("00")).ToArray();
        monthPicker.SelectedIndex = month - 1;
        SemanticProperties.SetDescription(monthPicker, "Mes de la consulta");
        ClassicWorkspaceChrome.CompactPeriod(monthPicker, locator);
        monthPicker.SelectedIndexChanged += async (_, _) =>
        {
            var value = monthPicker.SelectedIndex + 1;
            if (value < 1 || value == this.month) return;
            this.month = value; UpdateTitle();
            await Guard(Load);
        };
        var footer = new Grid { Padding = new Thickness(8, 1), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        var left = new HorizontalStackLayout { Spacing = 8 };
        left.Children.Add(JournalsPage.ActionButton("Filtro", "gl_filter.png", Filter, 82));
        var monthBox = new HorizontalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center };
        monthBox.Children.Add(ClassicWorkspaceChrome.Text("Mes :", true)); monthBox.Children.Add(monthPicker);
        left.Children.Add(monthBox);
        footer.Add(left, 0, 0);
        var right = new HorizontalStackLayout { Spacing = 5 };
        right.Children.Add(JournalsPage.ActionButton("Exportar", "gl_query.png", () => Guard(Export), 90));
        right.Children.Add(JournalsPage.ActionButton("Imprimir", "gl_policy.png", () => Guard(async () => await Navigation.PushAsync(new ReportPreviewPage(Document(), false))), 90));
        right.Children.Add(JournalsPage.ActionButton("Salir", "gl_close.png", async () => await Navigation.PopAsync(), 82));
        right.Children.Add(JournalsPage.ActionButton("Ayuda", "gl_help.png", Help, 82));
        footer.Add(right, 1, 0);

        var body = new Grid { Padding = new Thickness(8, 6, 8, 0), RowSpacing = 0,
            RowDefinitions = { new RowDefinition(20), new RowDefinition(GridLength.Star), new RowDefinition(30) } };
        body.Add(tabRow, 0, 0); body.Add(panel, 0, 1); body.Add(footer, 0, 2);
        Content = ClassicWorkspaceChrome.Frame(menu, ClassicWorkspaceChrome.ModuleToolbar(Open, year, month), body,
            ClassicWorkspaceChrome.Status(db, "Recorriendo Registros"));
        Loaded += (_, _) => UpdateTitle();
    }

    private static Grid Columns()
    {
        var grid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };
        foreach (var width in Widths) grid.ColumnDefinitions.Add(new ColumnDefinition(width));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); return grid;
    }

    private void UpdateTitle()
    {
        var name = CultureInfo.GetCultureInfo("es-MX").DateTimeFormat.GetMonthName(month);
        Title = $"Sistema de Contabilidad (GL 2000) - {company.Name} - [Consulta de Cuentas - {char.ToUpper(name[0]) + name[1..]}]";
        if (Window != null) ClassicWorkspaceChrome.WindowTitle(Window, Title);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdateTitle();
        await Guard(Load);
    }

    private async Task Load()
    {
        var keep = selected?.Row.Code;
        balances = await db.Balances(company.Id, year, month);
        Render(keep);
    }

    private string SortKey(BalanceRow row) => activeTab == 1 ? row.Description : row.Code;

    private List<BalanceRow> Filtered() => filter.Length == 0 ? balances : balances.Where(b =>
        b.Code.Contains(filter, StringComparison.OrdinalIgnoreCase) || b.Description.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();

    private void Render(string? keep = null)
    {
        selected = null;
        visibleRows = Filtered().OrderBy(SortKey, StringComparer.CurrentCultureIgnoreCase).ThenBy(b => b.Code, StringComparer.Ordinal)
            .Select(b => new BalanceDisplayRow(b, b.IsDetail)).ToList();
        table.ItemsSource = visibleRows;
        var target = visibleRows.FirstOrDefault(r => r.Row.Code == keep) ?? visibleRows.FirstOrDefault();
        table.SelectedItem = target;
        if (target != null) table.ScrollTo(target, position: ScrollToPosition.MakeVisible, animate: false);
        for (var i = 0; i < tabs.Count; i++) tabs[i].BackgroundColor = i == activeTab ? Gl2000Chrome.HeaderBlue : Color.FromArgb("#A5A7E5");
        count.Text = $"{visibleRows.Count} cuentas";
        Notice.Text = filter.Length > 0 ? $"Filtro: {filter}" : "";
    }

    private void Select(BalanceDisplayRow? item) { selected?.SetSelected(false); selected = item; selected?.SetSelected(true); }

    private void Move(int step)
    {
        if (visibleRows.Count == 0) return;
        var current = selected == null ? 0 : visibleRows.IndexOf(selected);
        var index = step == int.MinValue ? 0 : step == int.MaxValue ? visibleRows.Count - 1 : Math.Clamp(current + step, 0, visibleRows.Count - 1);
        table.SelectedItem = visibleRows[index]; table.ScrollTo(visibleRows[index], position: ScrollToPosition.MakeVisible, animate: false);
    }

    // Jumps to the first account whose number (tab Cuenta) or name (tab Nombre) starts with the text.
    private void Locate()
    {
        var text = locator.Text?.Trim() ?? "";
        if (text.Length == 0) return;
        var item = visibleRows.FirstOrDefault(r => SortKey(r.Row).StartsWith(text, StringComparison.CurrentCultureIgnoreCase));
        if (item == null) return;
        table.SelectedItem = item; table.ScrollTo(item, position: ScrollToPosition.Start, animate: false);
    }

    private Task Filter() => Guard(async () =>
    {
        var text = await DisplayPromptAsync("Filtro", "Cuenta o descripción. Deje vacío para mostrar todas.", "Aplicar", "Cancelar", initialValue: filter);
        if (text == null) return; filter = text.Trim(); Render(selected?.Row.Code);
    });

    private ReportDocument Document() => ReportFormatter.Balances(company.Name, year, month, filter, Filtered());

    private async Task Export()
    {
        var file = await ReportActions.Save(Document(), $"Saldos-{company.Id}-{year}-{month:00}", false);
        Notice.Text = $"CSV guardado: {file}";
    }

    private async Task OpenLedger()
    {
        if (selected == null) throw new InvalidOperationException("Seleccione una cuenta.");
        var code = selected.Row.Code;
        var account = (await db.Accounts(company.Id)).FirstOrDefault(a => a.Code == code) ?? throw new InvalidOperationException("La cuenta ya no está disponible.");
        await Navigation.PushAsync(new LedgerPage(db, company, year, month, account.Id));
    }

    private Task Help() => DisplayAlertAsync("Consulta de Cuentas",
        "Muestra por cuenta el saldo al inicio del mes, los cargos y créditos del mes y el saldo actual. Las cuentas de detalle aparecen en azul; " +
        "las superiores suman a sus subcuentas. Los importes negativos (acreedores) aparecen en rojo entre paréntesis. " +
        "El Localizador salta a la cuenta por número o nombre según la pestaña. Mes cambia el periodo consultado. Doble clic abre el mayor de la cuenta.", "Cerrar");

    private Task Open(string key) => Guard(async () =>
    {
        if (key == "exit") { await Navigation.PopAsync(); return; }
        if (key == "balances") { await Load(); return; }
        if (key == "accumulate") { await MenuActions.Accumulate(this); return; }
        await Gl2000Chrome.Navigate(this, db, company, year, month, key);
    });

    private sealed class BalanceDisplayRow : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly Color[] normal;
        public BalanceRow Row { get; }
        public string[] Cells { get; }
        public Color[] Colors { get; private set; }
        public Color Background { get; private set; } = Gl2000Chrome.RowYellow;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public BalanceDisplayRow(BalanceRow row, bool detail)
        {
            Row = row;
            long[] amounts = [row.Opening, row.Debits, row.Credits, row.Closing];
            Cells = [row.Code, row.Description, .. amounts.Select(AccountingUi.Money)];
            var text = detail ? DetailBlue : Microsoft.Maui.Graphics.Colors.Black;
            normal = [text, text, .. amounts.Select(a => a < 0 ? Gl2000Chrome.NegativeRed : Microsoft.Maui.Graphics.Colors.Black)];
            Colors = normal;
        }

        public void SetSelected(bool value)
        {
            Background = value ? Color.FromArgb("#0078D7") : Gl2000Chrome.RowYellow;
            Colors = value ? Enumerable.Repeat(Microsoft.Maui.Graphics.Colors.White, normal.Length).ToArray() : normal;
            PropertyChanged?.Invoke(this, new(nameof(Background))); PropertyChanged?.Invoke(this, new(nameof(Colors)));
        }
    }
}
