using GestionLibros.Data;
using GestionLibros.Models;
using GestionLibros.Reporting;

namespace GestionLibros.Views;

// GL2000 report windows (Balanza, Saldos, Auxiliares de Mayor): a small, movable window opened on top
// of whatever screen is showing, never a new screen. Imprimir sends the GL-format report to the
// preview (Pantalla) or the browser print dialog (Impresora); Exportar writes a CSV.
internal abstract class ReportDialog : Grid
{
    private static readonly Color FieldGray = Color.FromArgb("#E0E0E0");
    protected readonly Page Owner;
    protected readonly AppDatabase Db;
    protected readonly Company Company;
    protected readonly int Year, Month;
    protected readonly AbsoluteLayout Form = new();
    protected readonly Entry ExportName;
    protected readonly Label Status = ClassicWorkspaceChrome.Text("");
    protected bool ToPrinter, Monochrome;
    private List<Account>? accounts;
    private bool busy;

    protected ReportDialog(Page owner, AppDatabase db, Company company, int year, int month, string caption, string exportDefault, double width, double height)
    {
        Owner = owner; Db = db; Company = company; Year = year; Month = month;
        BackgroundColor = Color.FromArgb("#01000000");
        ExportName = TextField(exportDefault, Color.FromArgb("#F0F0F0"), "Archivo a exportar");
        Form.HeightRequest = height;
        Status.LineBreakMode = LineBreakMode.TailTruncation;
        Add(ClassicDialog.ChildWindow(ClassicWorkspaceChrome.Text(caption),
            new ContentView { Padding = new Thickness(4, 2, 4, 4), Content = Form }, width, Close));
    }

    // Opens the window over the page's current content (spanning all its rows), replacing any other one.
    internal static void Show(Page page, Grid dialog)
    {
        if (page is not ContentPage host) return;
        if (host.Content is not Grid root) { root = new Grid { Children = { host.Content } }; host.Content = root; }
        foreach (var open in root.Children.Where(c => c is ReportDialog or PolicyTypesDialog).ToList()) root.Remove(open);
        Grid.SetRowSpan(dialog, Math.Max(1, root.RowDefinitions.Count));
        Grid.SetColumnSpan(dialog, Math.Max(1, root.ColumnDefinitions.Count));
        root.Add(dialog);
    }

    protected void Close() { if (!busy && Parent is Grid root) root.Remove(this); }

    protected abstract Task<ReportDocument> Document();

    protected void Put(View view, double x, double y, double width, double height)
    {
        AbsoluteLayout.SetLayoutBounds(view, new Rect(x, y, width, height));
        Form.Children.Add(view);
    }

    protected static Entry TextField(string text, Color background, string name)
    {
        var entry = new Entry { Text = text, MaxLength = 60, FontFamily = "Arial", FontSize = 11, TextColor = Colors.Black,
            BackgroundColor = background, HeightRequest = 18 };
        SemanticProperties.SetDescription(entry, name);
        ClassicWorkspaceChrome.CompactEntry(entry);
        return entry;
    }

    protected static Border Box(View content, Color background) =>
        new() { Content = content, Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, BackgroundColor = background };

    protected static Border Group(View content) => new() { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Content = content };

    // Salida: Pantalla / Impresora, drawn as compact option marks like the original radio group.
    protected View OutputGroup()
    {
        var screen = Choice(); var printer = Choice();
        void Show() { screen.Text = (ToPrinter ? "( )" : "(•)") + "  Pantalla"; printer.Text = (ToPrinter ? "(•)" : "( )") + "  Impresora"; }
        Show();
        Tap(screen, "Pantalla", () => { ToPrinter = false; Show(); });
        Tap(printer, "Impresora", () => { ToPrinter = true; Show(); });
        return Group(new VerticalStackLayout { Spacing = 6, Padding = new Thickness(8, 2), Children = { ClassicWorkspaceChrome.Text("Salida :", true), screen, printer } });
    }

    protected View MonochromeChoice()
    {
        var label = Choice();
        void Show() => label.Text = (Monochrome ? "[X]" : "[  ]") + "  Impresión B & N";
        Show();
        Tap(label, "Impresión B y N", () => { Monochrome = !Monochrome; Show(); });
        return label;
    }

    protected static Picker LevelPicker()
    {
        var picker = new Picker { FontFamily = "Arial", FontSize = 11, TextColor = Colors.Black, BackgroundColor = Colors.White,
            HeightRequest = 20, MinimumHeightRequest = 0, ItemsSource = Enumerable.Range(1, 9).Select(i => i.ToString()).ToList(), SelectedIndex = 8 };
        SemanticProperties.SetDescription(picker, "Nivel máximo");
        ClassicWorkspaceChrome.CompactPicker(picker);
        return picker;
    }

    // Selección de Cuentas: Inicio / Final with a lookup button each.
    protected View AccountRange(Entry first, Entry last, bool lookups)
    {
        var grid = new Grid { Padding = new Thickness(8, 2), RowSpacing = 5, ColumnSpacing = 6,
            ColumnDefinitions = { new ColumnDefinition(40), new ColumnDefinition(GridLength.Star), new ColumnDefinition(lookups ? 26 : 0) },
            RowDefinitions = { new RowDefinition(18), new RowDefinition(20), new RowDefinition(20) } };
        var title = ClassicWorkspaceChrome.Text("Seleccion de Cuentas:", true);
        grid.Add(title, 0, 0); Grid.SetColumnSpan(title, 3);
        grid.Add(ClassicWorkspaceChrome.Text("Inicio:", true), 0, 1); grid.Add(Box(first, FieldGray), 1, 1);
        grid.Add(ClassicWorkspaceChrome.Text("Final:", true), 0, 2); grid.Add(Box(last, FieldGray), 1, 2);
        if (lookups)
        {
            grid.Add(JournalsPage.ActionButton("", "gl_search.png", () => PickAccount(first), 26), 2, 1);
            grid.Add(JournalsPage.ActionButton("", "gl_search.png", () => PickAccount(last), 26), 2, 2);
        }
        return Group(grid);
    }

    protected static Entry RangeField(string name) => TextField("", FieldGray, name);

    private async Task PickAccount(Entry target)
    {
        accounts ??= await Db.Accounts(Company.Id);
        var typed = (target.Text ?? "").Trim();
        var matches = accounts.Where(a => a.Code.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(typed, StringComparison.CurrentCultureIgnoreCase))
            .Take(60).Select(a => $"{a.Code}  {a.Description}").ToArray();
        if (matches.Length == 0) { Status.Text = "No hay cuentas que coincidan."; return; }
        var choice = await Owner.DisplayActionSheetAsync("Cuentas", "Cancelar", null, matches);
        if (choice != null && matches.Contains(choice)) target.Text = choice[..choice.IndexOf("  ", StringComparison.Ordinal)];
    }

    protected View PrintButton() => JournalsPage.ActionButton("Imprimir", "gl_policy.png", () => Run(Print), 90);
    protected View CancelButton() => JournalsPage.ActionButton("Cancelar", "login_cancel.png", () => { Close(); return Task.CompletedTask; }, 90);
    protected View ExportButton() => JournalsPage.ActionButton("Exportar", "gl_query.png", () => Run(Export), 90);

    private static Label Choice() => ClassicWorkspaceChrome.Text("", true);

    private static void Tap(Label label, string name, Action action)
    {
        var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => action(); label.GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(label, name);
    }

    private async Task Run(Func<Task> work)
    {
        if (busy) return;
        busy = true; Status.TextColor = Colors.Black; Status.Text = "Generando reporte…";
        try { await work(); }
        catch (InvalidOperationException ex) { Status.TextColor = Gl2000Chrome.NegativeRed; Status.Text = ex.Message; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Status.TextColor = Gl2000Chrome.NegativeRed; Status.Text = "No se pudo generar el reporte."; }
        finally { busy = false; }
    }

    private async Task Print()
    {
        var document = await Document();
        if (ToPrinter) { await ReportActions.Print(document, Monochrome); Status.Text = "Reporte enviado al navegador para imprimir."; }
        else { Status.Text = ""; await Owner.Navigation.PushAsync(new ReportPreviewPage(document, Monochrome)); }
    }

    private async Task Export()
    {
        var name = (ExportName.Text ?? "").Trim();
        if (name.Length == 0) throw new InvalidOperationException("Escriba el nombre del archivo a exportar.");
        var file = await ReportActions.Save(await Document(), name, false);
        Status.Text = "Exportado: " + Path.GetFileName(file);
        await Owner.DisplayAlertAsync("Exportar", $"Reporte exportado en:\n{file}", "Aceptar");
    }

    // Toolbar keys handled as report windows instead of new screens.
    internal static ReportDialog? For(string key, Page page, AppDatabase db, Company company, int year, int month) => key switch
    {
        // Like the GL listing: accounts without balance or movements in the month are left out.
        "trial" => new LevelReportDialog(page, db, company, year, month, "Reporte Balanza de Comprobación", "Balanza",
            async level => ReportFormatter.TrialBalance(await db.TrialBalance(company.Id, year, month, level, includeZeroAccounts: false))),
        "balance-sheet" => new LevelReportDialog(page, db, company, year, month, "Reporte Balance General", "BalanceGeneral",
            async level => ReportFormatter.BalanceSheet(await db.TrialBalance(company.Id, year, month, level, includeZeroAccounts: false))),
        "income-statement" => new LevelReportDialog(page, db, company, year, month, "Reporte Estado de Resultados", "Resultados",
            async level => ReportFormatter.IncomeStatement(await db.IncomeStatement(company.Id, year, month, level))),
        "saldos" => new AccountBalancesDialog(page, db, company, year, month),
        "ledger" => new LedgerDialog(page, db, company, year, month),
        "policy-print" => new PolicyPrintDialog(page, db, company, year, month),
        _ => null,
    };
}

// Reports whose only option is Nivel Maximo: Balanza de Comprobación, Balance General, Estado de Resultados.
internal sealed class LevelReportDialog : ReportDialog
{
    private readonly Picker level = LevelPicker();
    private readonly Func<int, Task<ReportDocument>> build;

    public LevelReportDialog(Page owner, AppDatabase db, Company company, int year, int month, string caption, string exportDefault,
        Func<int, Task<ReportDocument>> build)
        : base(owner, db, company, year, month, caption, exportDefault, 292, 166)
    {
        this.build = build;
        Put(OutputGroup(), 8, 4, 104, 70);
        Put(ClassicWorkspaceChrome.Text("Nivel Maximo :", true), 124, 6, 100, 18);
        Put(level, 132, 26, 56, 22);
        Put(MonochromeChoice(), 48, 80, 150, 18);
        Put(ClassicWorkspaceChrome.Text("Archivo a Exportar:", true), 8, 102, 150, 18);
        Put(Box(ExportName, Color.FromArgb("#F0F0F0")), 8, 122, 166, 20);
        Put(PrintButton(), 186, 58, 90, 25); Put(CancelButton(), 186, 88, 90, 25); Put(ExportButton(), 186, 118, 90, 25);
        Put(Status, 8, 148, 270, 16);
    }

    protected override Task<ReportDocument> Document() => build(level.SelectedIndex + 1);
}

// "Reporte de Saldos de Cuentas": only the balance of the selected accounts.
internal sealed class AccountBalancesDialog : ReportDialog
{
    private readonly Picker level = LevelPicker();
    private readonly Entry first = RangeField("Cuenta inicial");
    private readonly Entry last = RangeField("Cuenta final");

    public AccountBalancesDialog(Page owner, AppDatabase db, Company company, int year, int month)
        : base(owner, db, company, year, month, "Reporte de Saldos de Cuentas", "Saldos", 300, 240)
    {
        Put(OutputGroup(), 8, 4, 150, 70);
        Put(ClassicWorkspaceChrome.Text("Nivel Maximo :", true), 172, 10, 100, 18);
        Put(level, 180, 32, 56, 22);
        Put(MonochromeChoice(), 160, 78, 130, 18);
        Put(AccountRange(first, last, lookups: true), 8, 92, 170, 76);
        Put(PrintButton(), 196, 112, 90, 25); Put(CancelButton(), 196, 142, 90, 25); Put(ExportButton(), 196, 172, 90, 25);
        Put(ClassicWorkspaceChrome.Text("Archivo a Exportar:", true), 8, 174, 150, 18);
        Put(Box(ExportName, Color.FromArgb("#F0F0F0")), 8, 194, 180, 20);
        Put(Status, 8, 220, 280, 16);
    }

    protected override async Task<ReportDocument> Document()
    {
        var from = (first.Text ?? "").Trim(); var through = (last.Text ?? "").Trim();
        if (from.Length > 0 && through.Length > 0 && string.CompareOrdinal(from, through) > 0)
            throw new InvalidOperationException("La cuenta inicial debe ser menor o igual a la final.");
        return ReportFormatter.AccountBalances(await Db.TrialBalance(Company.Id, Year, Month, level.SelectedIndex + 1, includeZeroAccounts: false), from, through);
    }
}

// "Reporte Auxiliares de Mayor": every partida of the selected accounts between two months.
internal sealed class LedgerDialog : ReportDialog
{
    private static readonly string[] MonthNames = ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio",
        "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"];
    private readonly Picker fromMonth = MonthPicker("Mes inicial");
    private readonly Picker throughMonth = MonthPicker("Mes final");
    private readonly Entry first = RangeField("Cuenta inicial");
    private readonly Entry last = RangeField("Cuenta final");

    public LedgerDialog(Page owner, AppDatabase db, Company company, int year, int month)
        : base(owner, db, company, year, month, "Reporte Auxiliares de Mayor", "Mayor", 340, 232)
    {
        fromMonth.SelectedIndex = throughMonth.SelectedIndex = month - 1;
        Put(OutputGroup(), 8, 4, 150, 80);
        var period = new Grid { Padding = new Thickness(6, 2), RowSpacing = 8, ColumnSpacing = 4,
            ColumnDefinitions = { new ColumnDefinition(36), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(16), new RowDefinition(22), new RowDefinition(22) } };
        var title = ClassicWorkspaceChrome.Text("Periodo:", true);
        period.Add(title, 0, 0); Grid.SetColumnSpan(title, 2);
        period.Add(ClassicWorkspaceChrome.Text("Inicio:", true), 0, 1); period.Add(fromMonth, 1, 1);
        period.Add(ClassicWorkspaceChrome.Text("Fin :", true), 0, 2); period.Add(throughMonth, 1, 2);
        Put(Group(period), 166, 4, 158, 80);
        Put(AccountRange(first, last, lookups: true), 8, 92, 160, 76);
        Put(PrintButton(), 232, 94, 90, 25); Put(CancelButton(), 232, 122, 90, 25); Put(ExportButton(), 232, 150, 90, 25);
        Put(ClassicWorkspaceChrome.Text("Archivo a Exportar:", true), 8, 184, 120, 20);
        Put(Box(ExportName, Color.FromArgb("#F0F0F0")), 126, 184, 196, 20);
        Put(Status, 8, 210, 320, 16);
    }

    internal static Picker MonthPicker(string name)
    {
        var picker = new Picker { FontFamily = "Arial", FontSize = 11, TextColor = Colors.Black, BackgroundColor = Colors.White,
            HeightRequest = 22, MinimumHeightRequest = 0, ItemsSource = MonthNames };
        SemanticProperties.SetDescription(picker, name);
        ClassicWorkspaceChrome.CompactPicker(picker);
        return picker;
    }

    protected override async Task<ReportDocument> Document() =>
        ReportFormatter.LedgerRange(await Db.LedgerRange(Company.Id, Year, fromMonth.SelectedIndex + 1, throughMonth.SelectedIndex + 1,
            first.Text ?? "", last.Text ?? ""));
}

// "Impresión de Pólizas": every póliza of a range of months, optionally of one type.
internal sealed class PolicyPrintDialog : ReportDialog
{
    private readonly Picker fromMonth = LedgerDialog.MonthPicker("Mes inicial");
    private readonly Picker throughMonth = LedgerDialog.MonthPicker("Mes final");
    private readonly Entry type = TextField("", Color.FromArgb("#E0E0E0"), "Tipo de póliza");

    public PolicyPrintDialog(Page owner, AppDatabase db, Company company, int year, int month)
        : base(owner, db, company, year, month, "Impresión de Pólizas", "Polizas", 340, 196)
    {
        fromMonth.SelectedIndex = throughMonth.SelectedIndex = month - 1;
        Put(OutputGroup(), 8, 4, 150, 80);
        var period = new Grid { Padding = new Thickness(6, 2), RowSpacing = 8, ColumnSpacing = 4,
            ColumnDefinitions = { new ColumnDefinition(36), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(16), new RowDefinition(22), new RowDefinition(22) } };
        var title = ClassicWorkspaceChrome.Text("Periodo:", true);
        period.Add(title, 0, 0); Grid.SetColumnSpan(title, 2);
        period.Add(ClassicWorkspaceChrome.Text("Inicio:", true), 0, 1); period.Add(fromMonth, 1, 1);
        period.Add(ClassicWorkspaceChrome.Text("Fin :", true), 0, 2); period.Add(throughMonth, 1, 2);
        Put(Group(period), 166, 4, 158, 80);
        Put(ClassicWorkspaceChrome.Text("Tipo (vacío = todos):", true), 8, 94, 140, 18);
        Put(Box(type, Color.FromArgb("#E0E0E0")), 8, 114, 60, 20);
        Put(PrintButton(), 232, 92, 90, 25); Put(CancelButton(), 232, 120, 90, 25); Put(ExportButton(), 232, 148, 90, 25);
        Put(ClassicWorkspaceChrome.Text("Archivo a Exportar:", true), 8, 144, 120, 18);
        Put(Box(ExportName, Color.FromArgb("#F0F0F0")), 8, 162, 196, 20);
        Put(Status, 8, 182, 320, 14);
    }

    protected override async Task<ReportDocument> Document()
    {
        var from = fromMonth.SelectedIndex + 1; var through = throughMonth.SelectedIndex + 1;
        var policies = await Db.PoliciesForPrint(Company.Id, Year, from, through, type.Text ?? "");
        if (policies.Count == 0) throw new InvalidOperationException("No hay pólizas en ese periodo.");
        return ReportFormatter.Policies(Company.Name, Year, from, through, policies);
    }
}

// Archivos → Tipos de Pólizas: small list window to add, change or delete types (code + description).
internal sealed class PolicyTypesDialog : Grid
{
    private readonly AppDatabase db;
    private readonly Company company;
    private readonly CollectionView list = new() { SelectionMode = SelectionMode.Single, HeightRequest = 150, BackgroundColor = Gl2000Chrome.RowYellow };
    private readonly Entry code = new() { MaxLength = 20, FontFamily = "Arial", FontSize = 11, TextColor = Colors.Black, BackgroundColor = Gl2000Chrome.RowYellow, HeightRequest = 18 };
    private readonly Entry description = new() { MaxLength = 80, FontFamily = "Arial", FontSize = 11, TextColor = Colors.Black, BackgroundColor = Colors.White, HeightRequest = 18 };
    private readonly Label status = ClassicWorkspaceChrome.Text("");

    public PolicyTypesDialog(Page owner, AppDatabase db, Company company)
    {
        this.db = db; this.company = company;
        BackgroundColor = Color.FromArgb("#01000000");
        SemanticProperties.SetDescription(code, "Tipo"); SemanticProperties.SetDescription(description, "Descripción del tipo");
        ClassicWorkspaceChrome.CompactEntry(code); ClassicWorkspaceChrome.CompactEntry(description);
        list.ItemTemplate = new DataTemplate(() =>
        {
            var row = new Grid { HeightRequest = 16, ColumnDefinitions = { new ColumnDefinition(50), new ColumnDefinition(GridLength.Star) } };
            var c = ClassicWorkspaceChrome.Text("", true); c.SetBinding(Label.TextProperty, nameof(PolicyType.Code)); c.Padding = new Thickness(4, 0);
            var d = ClassicWorkspaceChrome.Text("", true); d.SetBinding(Label.TextProperty, nameof(PolicyType.Description));
            row.Add(c, 0, 0); row.Add(d, 1, 0);
            return row;
        });
        ClassicWorkspaceChrome.CompactRows(list);
        list.SelectionChanged += (_, e) =>
        {
            if (e.CurrentSelection.FirstOrDefault() is PolicyType t) { code.Text = t.Code; description.Text = t.Description; }
        };
        var header = new Grid { HeightRequest = 17, Background = ClassicWorkspaceChrome.ToolbarBrush(), ColumnDefinitions = { new ColumnDefinition(50), new ColumnDefinition(GridLength.Star) } };
        header.Add(ClassicWorkspaceChrome.Text(" Tipo", true), 0, 0); header.Add(ClassicWorkspaceChrome.Text("Descripción", true), 1, 0);
        var fields = new Grid { ColumnSpacing = 6, RowSpacing = 4, ColumnDefinitions = { new ColumnDefinition(80), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(20), new RowDefinition(20) } };
        var blue = Color.FromArgb("#0000FF");
        var l1 = ClassicWorkspaceChrome.Text("Tipo :", true); l1.TextColor = blue;
        var l2 = ClassicWorkspaceChrome.Text("Descripción :", true); l2.TextColor = blue;
        fields.Add(l1, 0, 0); fields.Add(new Border { Content = code, Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, WidthRequest = 60, HorizontalOptions = LayoutOptions.Start }, 1, 0);
        fields.Add(l2, 0, 1); fields.Add(new Border { Content = description, Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0 }, 1, 1);
        var buttons = new HorizontalStackLayout { Spacing = 5, HorizontalOptions = LayoutOptions.End };
        buttons.Children.Add(JournalsPage.ActionButton("Guardar", "login_accept.png", () => Run(async () =>
        {
            await db.SavePolicyType(company.Id, code.Text ?? "", description.Text ?? "");
            status.Text = "Tipo guardado."; await Load();
        }), 88));
        buttons.Children.Add(JournalsPage.ActionButton("Borrar", "gl_delete.png", () => Run(async () =>
        {
            if (!await owner.DisplayAlertAsync("Tipos de Pólizas", $"¿Borrar el tipo {code.Text}?", "Borrar", "Cancelar")) return;
            await db.DeletePolicyType(company.Id, (code.Text ?? "").Trim());
            code.Text = description.Text = ""; status.Text = "Tipo borrado."; await Load();
        }), 84));
        buttons.Children.Add(JournalsPage.ActionButton("Cerrar", "gl_close.png", () => { Close(); return Task.CompletedTask; }, 84));
        status.LineBreakMode = LineBreakMode.TailTruncation;
        var body = new VerticalStackLayout { Spacing = 6, Padding = new Thickness(8, 6), BackgroundColor = Color.FromArgb("#91F4CE"),
            Children = { new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Content = new VerticalStackLayout { Children = { header, list } } },
                fields, buttons, status } };
        Add(ClassicDialog.ChildWindow(ClassicWorkspaceChrome.Text("Tipos de Pólizas"), body, 380, Close));
        Loaded += async (_, _) => await Run(Load);
    }

    private async Task Load() => list.ItemsSource = await db.PolicyTypes(company.Id);

    private void Close() { if (Parent is Grid root) root.Remove(this); }

    private async Task Run(Func<Task> work)
    {
        status.TextColor = Colors.Black;
        try { await work(); }
        catch (InvalidOperationException ex) { status.TextColor = Gl2000Chrome.NegativeRed; status.Text = ex.Message; }
    }
}
