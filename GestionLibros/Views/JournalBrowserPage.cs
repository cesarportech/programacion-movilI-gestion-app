using System.ComponentModel;
using GestionLibros.Data;
using GestionLibros.Models;

namespace GestionLibros.Views;

public class JournalsPage : AccountingPage
{
    private readonly AppDatabase db;
    private readonly Company company;
    private readonly int year;
    private int month;
    private int activeTab;
    private string filter = "";
    private List<Journal> journals = [];
    private List<JournalDisplayRow> visibleRows = [];
    private JournalDisplayRow? selected;
    private readonly CollectionView table = new() { SelectionMode = SelectionMode.Single, BackgroundColor = Gl2000Chrome.RowYellow };
    private readonly Entry reference = new() { WidthRequest = 76, HeightRequest = 20, MinimumHeightRequest = 0,
        FontFamily = "Arial", FontSize = 11, TextColor = Colors.Black, BackgroundColor = Colors.White, Margin = 0 };
    private readonly Picker monthPicker = new() { WidthRequest = 51, HeightRequest = 20, MinimumHeightRequest = 0,
        FontFamily = "Arial", FontSize = 11, TextColor = Colors.Black, BackgroundColor = Colors.White };
    private readonly List<Button> tabs = [];
    private readonly Label count = ClassicWorkspaceChrome.Text("");
    private static readonly string[] Headers = ["TPol", "Ref.", "Fecha", "Concepto", "Beneficiario", "Banco", "Fecha Cambio", "Usuario", "Status"];
    private static readonly int[] Widths = [36, 58, 80, 216, 252, 44, 82, 88, 41];

    public JournalsPage(AppDatabase db, Company company, int year, int month) : base("Tabla de Asientos")
    {
        this.db = db; this.company = company; this.year = year; this.month = month;
        NavigationPage.SetHasNavigationBar(this, false);
        Title = $"Sistema de Contabilidad (GL 2000) - {company.Name} - [Tabla de Asientos]";
        Gl2000Chrome.ApplyMenus(this, Open);
        var exit = MenuBarItems[0].OfType<MenuFlyoutItem>().First(x => x.Text == "Salir");
        exit.Command = new Command(async () => await Navigation.PopAsync());
        var menu = ClassicWorkspaceChrome.Menu(this, MenuBarItems.ToArray());
        MenuBarItems.Clear();

        monthPicker.ItemsSource = Enumerable.Range(1, 12).Select(m => m.ToString("00")).ToArray();
        monthPicker.SelectedIndex = month - 1;
        ClassicWorkspaceChrome.CompactPeriod(monthPicker, reference);
        monthPicker.SelectedIndexChanged += (_, _) =>
        {
            var value = monthPicker.SelectedIndex + 1;
            if (value == this.month || value < 1) return;
            this.month = value; activeTab = value + 1;
            Render(selected?.Journal.Id);
        };
        var toolbar = new HorizontalStackLayout { Spacing = 1, Padding = new Thickness(3, 2, 0, 0) };
        foreach (var (caption, image, key) in new[] { ("Catálogo", "gl_catalog.png", "catalog"),
            ("Asientos", "gl_journals.png", "journals"), ("Acumular", "gl_accumulate.png", "accumulate"),
            ("Consulta", "gl_query.png", "balances"), ("Balanza", "gl_trial.png", "trial"),
            ("Saldos", "gl_balances.png", "saldos"), ("Mayor", "gl_ledger.png", "ledger"), ("Salir", "gl_exit.png", "exit") })
            toolbar.Children.Add(ClassicWorkspaceChrome.Tool(caption, image, () => Open(key)));
        toolbar.Children.Add(ClassicWorkspaceChrome.SignOutTool(() => Open("logout")));
        var period = new HorizontalStackLayout { Spacing = 5, Margin = new Thickness(67, 4, 0, 0), VerticalOptions = LayoutOptions.Start };
        period.Children.Add(ClassicWorkspaceChrome.Text("Mes", true)); period.Children.Add(monthPicker);
        period.Children.Add(ClassicWorkspaceChrome.Text("Año", true)); period.Children.Add(ClassicWorkspaceChrome.Text(year.ToString(), true));
        toolbar.Children.Add(period);

        var tabRow = new Grid { ColumnSpacing = 1, HeightRequest = 22 };
        var names = new[] { "Numero", "Mes", "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre" };
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var button = FlatButton(names[i], () =>
            {
                activeTab = index;
                if (index >= 2) { this.month = index - 1; monthPicker.SelectedIndex = this.month - 1; }
                Render(selected?.Journal.Id);
                return Task.CompletedTask;
            });
            button.HorizontalOptions = LayoutOptions.Fill;
            button.HeightRequest = 22;
            button.Padding = new Thickness(3, 0);
            button.FontAttributes = FontAttributes.Bold;
#if WINDOWS
            button.HandlerChanged += (_, _) =>
            {
                if (button.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Button nativeButton)
                    nativeButton.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left;
            };
#endif
            tabRow.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            tabRow.Add(button, i, 0); tabs.Add(button);
        }

        var gridHeader = Columns();
        gridHeader.HeightRequest = 17;
        gridHeader.BackgroundColor = Color.FromArgb("#D0D0D0");
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
            row.SetBinding(VisualElement.BackgroundColorProperty, nameof(JournalDisplayRow.Background));
            for (var i = 0; i < Headers.Length; i++)
            {
                var label = ClassicWorkspaceChrome.Text("", true);
                label.FontSize = 11;
                label.Padding = new Thickness(2, 0);
                label.LineBreakMode = LineBreakMode.NoWrap;
                label.SetBinding(Label.TextProperty, $"Cells[{i}]");
                label.SetBinding(Label.TextColorProperty, nameof(JournalDisplayRow.Foreground));
                if (i is 0 or 8) label.HorizontalTextAlignment = TextAlignment.Center;
                if (i is 2 or 6) label.HorizontalTextAlignment = TextAlignment.End;
                row.Add(new Border { StrokeThickness = 0, Content = label, Padding = 0 }, i, 0);
                row.Add(new BoxView { WidthRequest = 1, HorizontalOptions = LayoutOptions.End,
                    Color = Color.FromArgb("#909078"), InputTransparent = true }, i, 0);
            }
            var doubleClick = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
            doubleClick.Tapped += async (_, _) =>
            {
                if (row.BindingContext is JournalDisplayRow item) { Select(item); await Guard(Edit); }
            };
            row.GestureRecognizers.Add(doubleClick);
            return row;
        });
        table.SelectionChanged += (_, e) => Select(e.CurrentSelection.FirstOrDefault() as JournalDisplayRow);
#if WINDOWS
        table.HandlerChanged += (_, _) =>
        {
            if (table.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ListViewBase list)
            {
                var style = new Microsoft.UI.Xaml.Style(typeof(Microsoft.UI.Xaml.Controls.ListViewItem));
                style.Setters.Add(new Microsoft.UI.Xaml.Setter(Microsoft.UI.Xaml.FrameworkElement.MinHeightProperty, 14d));
                style.Setters.Add(new Microsoft.UI.Xaml.Setter(Microsoft.UI.Xaml.Controls.Control.PaddingProperty, new Microsoft.UI.Xaml.Thickness(0)));
                style.Setters.Add(new Microsoft.UI.Xaml.Setter(Microsoft.UI.Xaml.Controls.Control.HorizontalContentAlignmentProperty, Microsoft.UI.Xaml.HorizontalAlignment.Stretch));
                style.Setters.Add(new Microsoft.UI.Xaml.Setter(Microsoft.UI.Xaml.Controls.Control.VerticalContentAlignmentProperty, Microsoft.UI.Xaml.VerticalAlignment.Stretch));
                list.ItemContainerStyle = style;
            }
        };
#endif
        var data = new Grid { RowSpacing = 0, RowDefinitions = { new RowDefinition(17), new RowDefinition(GridLength.Star) }, MinimumWidthRequest = 940 };
        var ruledArea = Columns(); ruledArea.BackgroundColor = Gl2000Chrome.RowYellow;
        for (var i = 0; i < Widths.Length; i++)
            ruledArea.Add(new BoxView { WidthRequest = 1, Color = Color.FromArgb("#909078"), HorizontalOptions = LayoutOptions.End, InputTransparent = true }, i, 0);
        table.BackgroundColor = Colors.Transparent;
        ruledArea.Add(table); Grid.SetColumnSpan(table, Widths.Length + 1);
        data.Add(gridHeader, 0, 0); data.Add(ruledArea, 0, 1);
        var scroll = new ScrollView { Margin = new Thickness(7, 0, 7, 0), Orientation = ScrollOrientation.Horizontal, Content = data, HorizontalScrollBarVisibility = ScrollBarVisibility.Always };
        scroll.SizeChanged += (_, _) => { if (scroll.Width > 0) data.WidthRequest = Math.Max(940, scroll.Width); };

        var navigation = new HorizontalStackLayout { Spacing = 1, HeightRequest = 18, BackgroundColor = Color.FromArgb("#F0F0F0") };
        foreach (var (caption, step) in new[] { ("|◀", int.MinValue), ("◀", -1), ("▶", 1), ("▶|", int.MaxValue) })
            {
            var nav = FlatButton(caption, () => { Move(step); return Task.CompletedTask; }, 19);
            nav.BackgroundColor = Colors.LightGray; navigation.Children.Add(nav);
        }
        count.Margin = new Thickness(8, 0); navigation.Children.Add(count);

        var lower = new Grid { Padding = new Thickness(16, 4), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        var policy = new HorizontalStackLayout { Spacing = 6 };
        policy.Children.Add(ClassicWorkspaceChrome.Text("Póliza:", true)); policy.Children.Add(reference);
        reference.Completed += async (_, _) => await Guard(FindReference);
        policy.Children.Add(ActionButton("Póliza", "gl_policy.png", () => Guard(FindReference), 94));
        lower.Add(policy, 0, 0);
        var edits = new HorizontalStackLayout { Spacing = 4 };
        edits.Children.Add(ActionButton("Búsqueda", "gl_search.png", Search, 100));
        edits.Children.Add(ActionButton("Agregar", "gl_add.png", () => Guard(() => Navigation.PushAsync(new JournalEditor(db, company, new Journal { Date = new DateTime(year, this.month, 1) })))));
        edits.Children.Add(ActionButton("Cambiar", "gl_change.png", () => Guard(Edit)));
        edits.Children.Add(ActionButton("Borrar", "gl_delete.png", () => Guard(Delete)));
        lower.Add(edits, 1, 0);

        var panel = new Grid { Margin = new Thickness(8, 8, 8, 2), Padding = 1, BackgroundColor = Gl2000Chrome.HeaderBlue,
            RowSpacing = 0, RowDefinitions = { new RowDefinition(22), new RowDefinition(25), new RowDefinition(GridLength.Star), new RowDefinition(18), new RowDefinition(36) } };
        panel.Add(tabRow, 0, 0);
        panel.Add(Notice, 0, 1); Notice.FontSize = 11; Notice.Padding = new Thickness(5, 2);
        panel.Add(scroll, 0, 2); panel.Add(navigation, 0, 3); panel.Add(lower, 0, 4);
        var footer = new Grid { Padding = new Thickness(16, 1, 8, 1), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        var left = new HorizontalStackLayout { Spacing = 5 };
        left.Children.Add(ActionButton("Filtro", "gl_filter.png", Search, 82));
        var duplicate = ActionButton("Duplicar", "gl_add.png", () => Guard(Duplicate), 82);
        left.Children.Add(duplicate); footer.Add(left, 0, 0);
        var right = new HorizontalStackLayout { Spacing = 5 };
        right.Children.Add(ActionButton("Salir", "gl_close.png", async () => await Navigation.PopAsync(), 82));
        right.Children.Add(ActionButton("Ayuda", "gl_help.png", Help, 82)); footer.Add(right, 1, 0);
        var body = new Grid { RowSpacing = 0, RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(30) } };
        body.Add(panel, 0, 0); body.Add(footer, 0, 1);
        var frame = new Grid { RowSpacing = 0, RowDefinitions = { new RowDefinition(20), new RowDefinition(57), new RowDefinition(GridLength.Star), new RowDefinition(21) } };
        frame.Add(menu, 0, 0);
        frame.Add(new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Background = ClassicWorkspaceChrome.ToolbarBrush(),
            Content = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = toolbar } }, 0, 1);
        frame.Add(Gl2000Chrome.Backdrop(body, "gl_wood.png", 59, 75), 0, 2);
        frame.Add(ClassicWorkspaceChrome.Status(db, "Recorriendo Registros"), 0, 3);
        Content = frame;
        Loaded += (_, _) => { if (Window != null) ClassicWorkspaceChrome.WindowTitle(Window, Title); };
    }

    private static Grid Columns()
    {
        var grid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };
        foreach (var width in Widths) grid.ColumnDefinitions.Add(new ColumnDefinition(width));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); return grid;
    }

    internal static Button FlatButton(string text, Func<Task> action, double width = -1)
    {
        var button = new Button { Text = text, FontFamily = "Arial", FontSize = 11, FontAutoScalingEnabled = false,
            TextColor = Colors.Black, CornerRadius = 0, BorderWidth = 1, BorderColor = Colors.White,
            BackgroundColor = Color.FromArgb("#A5A7E5"), MinimumWidthRequest = 0, MinimumHeightRequest = 0,
            HeightRequest = 18, Padding = 0 };
        if (width > 0) button.WidthRequest = width;
        button.Clicked += async (_, _) => await action(); return button;
    }

    internal static View ActionButton(string text, string image, Func<Task> action, double width = 84)
    {
        var content = new Grid { ColumnDefinitions = { new ColumnDefinition(25), new ColumnDefinition(GridLength.Star) }, Padding = new Thickness(3, 0) };
        content.Add(new Image { Source = image, WidthRequest = 24, HeightRequest = 24 }, 0, 0);
        var label = ClassicWorkspaceChrome.Text(text, true); label.TextDecorations = TextDecorations.Underline;
        content.Add(label, 1, 0);
        var button = FlatButton("", action); button.HeightRequest = 25; button.BackgroundColor = Colors.Transparent;
        SemanticProperties.SetDescription(button, text);
        var layout = new Grid { WidthRequest = width, HeightRequest = 25, Background = ClassicWorkspaceChrome.ToolbarBrush() };
        content.InputTransparent = true; layout.Add(content); layout.Add(button);
        button.Pressed += (_, _) => { content.TranslationY = 1; content.TranslationX = 1; };
        button.Released += (_, _) => { content.TranslationY = 0; content.TranslationX = 0; };
        return layout;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (Window != null) ClassicWorkspaceChrome.WindowTitle(Window, Title);
        await Guard(Load);
    }

    private async Task Load()
    {
        // The whole year is read once; changing tab or month only filters it in memory.
        var keep = selected?.Journal.Id;
        journals = await db.JournalsOfYear(company.Id, year);
        Render(keep);
    }

    private void Render(int? keep = null)
    {
        IEnumerable<Journal> result = activeTab < 2 ? journals : journals.Where(j => j.Month == month);
        if (!string.IsNullOrWhiteSpace(filter)) result = result.Where(j => $"{j.Type} {j.Reference} {j.Concept} {j.ChangedBy}".Contains(filter, StringComparison.CurrentCultureIgnoreCase));
        result = activeTab == 0 ? result.OrderBy(j => j.Type).ThenBy(j => j.Reference, StringComparer.OrdinalIgnoreCase) : result.OrderBy(j => j.Date).ThenBy(j => j.Reference);
        visibleRows = result.Select(j => new JournalDisplayRow(j)).ToList();
        table.ItemsSource = visibleRows;
        table.SelectedItem = visibleRows.FirstOrDefault(r => r.Journal.Id == keep) ?? visibleRows.FirstOrDefault();
        for (var i = 0; i < tabs.Count; i++) tabs[i].BackgroundColor = i == activeTab ? Gl2000Chrome.HeaderBlue : Color.FromArgb("#A5A7E5");
        count.Text = $"{visibleRows.Count} registros";
        Notice.Text = filter.Length > 0 ? $"Filtro: {filter}" : "";
    }

    private void Select(JournalDisplayRow? item)
    {
        selected?.SetSelected(false); selected = item; selected?.SetSelected(true);
        reference.Text = selected?.Journal.Reference ?? "";
    }
    private void Move(int step)
    {
        if (visibleRows.Count == 0) return;
        var index = step == int.MinValue ? 0 : step == int.MaxValue ? visibleRows.Count - 1 : Math.Clamp(visibleRows.IndexOf(selected!) + step, 0, visibleRows.Count - 1);
        table.SelectedItem = visibleRows[index]; table.ScrollTo(visibleRows[index]);
    }
    private Task FindReference()
    {
        var item = visibleRows.FirstOrDefault(r => r.Journal.Reference.Equals(reference.Text?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (item == null) throw new InvalidOperationException("No se encontró esa póliza en la vista actual.");
        table.SelectedItem = item; table.ScrollTo(item); return Task.CompletedTask;
    }
    private Task Search() => Guard(async () =>
    {
        var text = await DisplayPromptAsync("Búsqueda / Filtro", "Referencia, tipo, concepto o usuario. Deje vacío para mostrar todos.", "Aplicar", "Cancelar", initialValue: filter);
        if (text == null) return; filter = text.Trim(); Render();
    });
    private Task Edit() => selected == null ? throw new InvalidOperationException("Seleccione una póliza.") : Navigation.PushAsync(new JournalEditor(db, company, selected.Journal));
    private async Task Delete()
    {
        if (selected == null) throw new InvalidOperationException("Seleccione una póliza.");
        var journal = selected.Journal;
        if (await DisplayAlertAsync("Borrar póliza", $"¿Borrar {journal.Type}-{journal.Reference} y sus movimientos?", "Borrar", "Cancelar"))
        { await db.DeleteJournal(company.Id, journal.Id); await Load(); }
    }
    // Copies the selected póliza and its movements with a new reference (suggests the next number).
    private async Task Duplicate()
    {
        if (selected == null) throw new InvalidOperationException("Seleccione una póliza.");
        var journal = selected.Journal;
        var suggested = long.TryParse(journal.Reference, out var number) ? (number + 1).ToString() : journal.Reference + "-1";
        var reference = await DisplayPromptAsync("Duplicar póliza", $"Nueva referencia para la copia de {journal.Type}-{journal.Reference} (misma fecha, concepto y movimientos):",
            "Duplicar", "Cancelar", initialValue: suggested, maxLength: 60);
        if (string.IsNullOrWhiteSpace(reference)) return;
        await db.DuplicateJournal(company.Id, journal.Id, reference, journal.Date);
        await Load();
        Notice.Text = $"Póliza duplicada como {journal.Type}-{reference.Trim()}.";
    }

    private Task Help() => DisplayAlertAsync("Tabla de Asientos", "Numero muestra las pólizas del año por tipo y referencia; Mes las ordena por fecha. Las pestañas de Enero a Diciembre filtran el mes. Búsqueda y Filtro buscan en la vista actual. Beneficiario, Banco y Status están pendientes en el modelo local; no se asignan valores ficticios. Duplicar copia la póliza seleccionada con sus movimientos bajo una nueva referencia.", "Cerrar");
    private Task Open(string key) => Guard(async () =>
    {
        if (key == "exit") { await Navigation.PopAsync(); return; }
        if (key == "journals") { await Load(); return; }
        if (key == "accumulate") { await MenuActions.Accumulate(this); return; }
        await Gl2000Chrome.Navigate(this, db, company, year, month, key);
    });

    private sealed class JournalDisplayRow(Journal journal) : INotifyPropertyChanged
    {
        public Journal Journal { get; } = journal;
        public string[] Cells { get; } = [journal.Type, journal.Reference, journal.Date.ToString("d/M/yyyy"), journal.Concept,
            "", "", journal.ChangedAt == default ? "" : journal.ChangedAt.ToString("d/M/yyyy"), journal.ChangedBy, ""];
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
