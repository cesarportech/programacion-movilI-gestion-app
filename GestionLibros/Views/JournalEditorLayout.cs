using GestionLibros.Models;

namespace GestionLibros.Views;

public partial class JournalEditor
{
    private readonly Label debitTotal = BlueText("0.00");
    private readonly Label creditTotal = BlueText("0.00");
    private static readonly int[] MovementWidths = [58, 175, 171, 97, 98];
    private ScrollView? movementScroll;

    private static Label BlueText(string text)
    {
        var label = ClassicWorkspaceChrome.Text(text, true); label.TextColor = Colors.Blue; return label;
    }

    private void BuildClassicEditor()
    {
        NavigationPage.SetHasNavigationBar(this, false);
        Title = $"Sistema de Contabilidad (GL 2000) - {company.Name} - [{(original.Id == 0 ? "Agregando un Asiento" : "Cambiando un Asiento")}]";
        Gl2000Chrome.ApplyMenus(this, OpenModule);
        MenuBarItems[0].OfType<MenuFlyoutItem>().First(x => x.Text == "Salir").Command = new Command(async () => await Navigation.PopAsync());
        var menu = ClassicWorkspaceChrome.Menu(this, MenuBarItems.ToArray()); MenuBarItems.Clear();
        var tools = new HorizontalStackLayout { Spacing = 1, Padding = new Thickness(3, 2, 0, 0) };
        foreach (var (caption, image, key) in new[] { ("Catálogo", "gl_catalog.png", "catalog"),
            ("Asientos", "gl_journals.png", "journals"), ("Acumular", "gl_accumulate.png", "accumulate"),
            ("Consulta", "gl_query.png", "balances"), ("Balanza", "gl_trial.png", "trial"),
            ("Saldos", "gl_balances.png", "balances"), ("Mayor", "gl_ledger.png", "ledger"), ("Salir", "gl_exit.png", "exit") })
            tools.Children.Add(ClassicWorkspaceChrome.Tool(caption, image, () => OpenModule(key)));
        var period = new HorizontalStackLayout { Spacing = 5, Margin = new Thickness(67, 4, 0, 0), VerticalOptions = LayoutOptions.Start };
        period.Children.Add(ClassicWorkspaceChrome.Text("Mes", true));
        period.Children.Add(new Border { Padding = new Thickness(3, 0), Stroke = Colors.Gray, BackgroundColor = Colors.White,
            Content = ClassicWorkspaceChrome.Text(original.Date.Month.ToString("00"), true), WidthRequest = 51, HeightRequest = 18 });
        period.Children.Add(ClassicWorkspaceChrome.Text("Año", true)); period.Children.Add(ClassicWorkspaceChrome.Text(original.Date.Year.ToString(), true)); tools.Children.Add(period);

        var fields = new AbsoluteLayout { HeightRequest = 106 };
        void Put(View view, double x, double y, double width, double height)
        { AbsoluteLayout.SetLayoutBounds(view, new Rect(x, y, width, height)); fields.Children.Add(view); }
        foreach (var (caption, y) in new[] { ("Tipo de Póliza:", 8d), ("Referencia:", 31d), ("Fecha:", 54d), ("Concepto:", 79d) })
            Put(BlueText(caption), 10, y, 84, 20);
        Put(CompactField(type, Gl2000Chrome.RowYellow), 94, 8, 28, 20);
        Put(JournalsPage.ActionButton("", "gl_search.png", () => Guard(FindType), 24), 126, 6, 24, 24);
        Put(CompactField(reference, Gl2000Chrome.RowYellow), 94, 31, 55, 20);
        date.Format = "d/M/yyyy"; date.FontFamily = "Arial"; date.FontSize = 11;
        date.TextColor = Colors.Black; date.BackgroundColor = Gl2000Chrome.RowYellow;
        date.MinimumHeightRequest = 0; date.HeightRequest = 20;
#if WINDOWS
        date.HandlerChanged += (_, _) =>
        {
            if (date.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.CalendarDatePicker picker)
            { picker.MinHeight = picker.MinWidth = 0; picker.Padding = new Microsoft.UI.Xaml.Thickness(0); picker.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(0); }
        };
#endif
        Put(date, 94, 54, 124, 20);
        Put(CompactField(concept, Colors.White), 94, 80, 270, 20);
        AutomationProperties.SetName(type, "Tipo de póliza"); AutomationProperties.SetName(reference, "Referencia");
        AutomationProperties.SetName(date, "Fecha"); AutomationProperties.SetName(concept, "Concepto");

        rows.Spacing = 0; rows.Padding = 0; rows.MinimumWidthRequest = 0;
        var header = MovementColumns(); header.HeightRequest = 17; header.BackgroundColor = Colors.LightGray;
        var captions = new[] { "Cuenta", "Descripción", "Concepto", "Cargos", "Créditos" };
        for (var i = 0; i < captions.Length; i++)
        {
            var label = ClassicWorkspaceChrome.Text(captions[i], true); label.HorizontalTextAlignment = TextAlignment.Center;
            header.Add(new Border { Content = label, Padding = 0, Stroke = Colors.Gray, StrokeThickness = 1,
                Background = ClassicWorkspaceChrome.ToolbarBrush() }, i, 0);
        }
        var ruledArea = MovementColumns(); ruledArea.BackgroundColor = Gl2000Chrome.RowYellow;
        for (var i = 0; i < MovementWidths.Length; i++)
            ruledArea.Add(new BoxView { WidthRequest = 1, HorizontalOptions = LayoutOptions.End, Color = Color.FromArgb("#909078"), InputTransparent = true }, i, 0);
        movementScroll = new ScrollView { Content = rows, Orientation = ScrollOrientation.Vertical };
        ruledArea.Add(movementScroll); Grid.SetColumnSpan(movementScroll, 6);
        var data = new Grid { RowSpacing = 0, MinimumWidthRequest = 700, RowDefinitions = { new RowDefinition(17), new RowDefinition(GridLength.Star), new RowDefinition(17) } };
        data.Add(header, 0, 0); data.Add(ruledArea, 0, 1);
        var navigation = new HorizontalStackLayout { Spacing = 1, BackgroundColor = Color.FromArgb("#F0F0F0") };
        foreach (var (caption, step) in new[] { ("|◀", int.MinValue), ("◀", -1), ("▶", 1), ("▶|", int.MaxValue) })
        {
            var button = new Button { Text = caption, FontSize = 9, TextColor = Colors.Black, BackgroundColor = Colors.LightGray,
                CornerRadius = 0, Padding = 0, MinimumWidthRequest = 0, MinimumHeightRequest = 0, WidthRequest = 19, HeightRequest = 17 };
            button.Clicked += async (_, _) =>
            {
                if (lines.Count == 0) return;
                var index = step == int.MinValue ? 0 : step == int.MaxValue ? lines.Count - 1 : Math.Clamp(lines.IndexOf(selected!) + step, 0, lines.Count - 1);
                selected = lines[index]; Render();
                await movementScroll.ScrollToAsync((Element)rows.Children[index], ScrollToPosition.MakeVisible, false);
            };
            navigation.Children.Add(button);
        }
        data.Add(navigation, 0, 2);
        var horizontal = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Always, Content = data };
        horizontal.SizeChanged += (_, _) => { if (horizontal.Width > 0) data.WidthRequest = Math.Max(700, horizontal.Width); };
        var table = new Border { Margin = new Thickness(5, 0), Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Content = horizontal };

        var actions = new Grid { ColumnDefinitions = { new ColumnDefinition(300), new ColumnDefinition(111), new ColumnDefinition(97), new ColumnDefinition(98), new ColumnDefinition(GridLength.Star) }, Padding = new Thickness(10, 3) };
        var edits = new HorizontalStackLayout { Spacing = 4 };
        edits.Children.Add(JournalsPage.ActionButton("Agregar", "gl_add.png", () => EditLine(null), 88));
        edits.Children.Add(JournalsPage.ActionButton("Cambiar", "gl_change.png", () => Guard(async () =>
        { if (selected == null) throw new InvalidOperationException("Seleccione un movimiento."); await PushLine(selected); }), 88));
        edits.Children.Add(JournalsPage.ActionButton("Borrar", "gl_delete.png", () => Guard(async () =>
        {
            if (selected == null) throw new InvalidOperationException("Seleccione un movimiento.");
            if (await DisplayAlertAsync("Quitar movimiento", "¿Quitar el movimiento de esta póliza? Se guardará al aceptar la póliza.", "Quitar", "Cancelar"))
            { lines.Remove(selected); selected = null; Render(); }
        }), 88));
        actions.Add(edits, 0, 0);
        sums.FontFamily = "Arial"; sums.FontSize = 11; sums.TextColor = Colors.Blue;
        actions.Add(sums, 1, 0);
        debitTotal.HorizontalTextAlignment = creditTotal.HorizontalTextAlignment = TextAlignment.End;
        debitTotal.Margin = creditTotal.Margin = new Thickness(0, 0, 6, 0);
        actions.Add(debitTotal, 2, 0); actions.Add(creditTotal, 3, 0);
        Notice.FontSize = 11; Notice.Margin = new Thickness(12, 0); actions.Add(Notice, 4, 0);

        var pane = new Grid { BackgroundColor = Color.FromArgb("#91F4CE"), RowSpacing = 0,
            RowDefinitions = { new RowDefinition(106), new RowDefinition(GridLength.Star), new RowDefinition(36) } };
        pane.Add(fields, 0, 0); pane.Add(table, 0, 1); pane.Add(actions, 0, 2);
        var tab = new Border { HorizontalOptions = LayoutOptions.Start, WidthRequest = 55, BackgroundColor = Color.FromArgb("#91F4CE"),
            Stroke = Colors.Gray, StrokeThickness = 1, Padding = new Thickness(3, 0), Content = ClassicWorkspaceChrome.Text("General", true) };
        var footer = new HorizontalStackLayout { HorizontalOptions = LayoutOptions.End, Spacing = 5, Padding = new Thickness(0, 2, 3, 0) };
        footer.Children.Add(JournalsPage.ActionButton("Aceptar", "gl_accept.png", () => Guard(async () =>
        {
            if (date.Date == null) throw new InvalidOperationException("Indique una fecha.");
            await db.SaveJournal(company.Id, new Journal { Id = original.Id, Date = date.Date.Value, Type = type.Text ?? "", Reference = reference.Text ?? "", Concept = concept.Text ?? "" }, lines);
            await Navigation.PopAsync();
        }), 84));
        footer.Children.Add(JournalsPage.ActionButton("Cancelar", "gl_exit.png", async () => await Navigation.PopAsync(), 86));
        footer.Children.Add(JournalsPage.ActionButton("Ayuda", "gl_help.png", () => DisplayAlertAsync("Agregar un asiento", "Complete tipo, referencia, fecha y concepto. Agregue los movimientos; los cargos y créditos deben coincidir. Aceptar guarda la póliza y sus movimientos. Cancelar descarta los cambios de esta pantalla.", "Cerrar"), 84));
        var body = new Grid { Padding = new Thickness(5, 4, 5, 0), RowSpacing = 0,
            RowDefinitions = { new RowDefinition(22), new RowDefinition(GridLength.Star), new RowDefinition(28) } };
        body.Add(tab, 0, 0); body.Add(new Border { Stroke = Colors.Gray, Padding = 0, StrokeThickness = 1, Content = pane }, 0, 1); body.Add(footer, 0, 2);
        var frame = new Grid { RowSpacing = 0, RowDefinitions = { new RowDefinition(20), new RowDefinition(57), new RowDefinition(GridLength.Star), new RowDefinition(21) } };
        frame.Add(menu, 0, 0);
        frame.Add(new Border { Stroke = Colors.Gray, Padding = 0, StrokeThickness = 1, Background = ClassicWorkspaceChrome.ToolbarBrush(),
            Content = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = tools } }, 0, 1);
        frame.Add(Gl2000Chrome.Backdrop(body, "gl_card.png", 33, 31), 0, 2);
        frame.Add(ClassicWorkspaceChrome.Status(db), 0, 3);
        Content = frame;
        Loaded += (_, _) => { if (Window != null) ClassicWorkspaceChrome.WindowTitle(Window, Title); };
    }

    private static Border CompactField(Entry entry, Color background)
    {
        entry.Placeholder = null; entry.FontFamily = "Arial"; entry.FontSize = 11; entry.FontAutoScalingEnabled = false;
        entry.TextColor = Colors.Black; entry.BackgroundColor = background; entry.MinimumHeightRequest = 0;
        entry.MinimumWidthRequest = 0; entry.HeightRequest = 18; entry.Margin = 0;
#if WINDOWS
        entry.HandlerChanged += (_, _) =>
        {
            if (entry.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox box)
            { box.MinHeight = box.MinWidth = 0; box.Padding = new Microsoft.UI.Xaml.Thickness(0); box.BorderThickness = new Microsoft.UI.Xaml.Thickness(0); }
        };
#endif
        return new Border { Content = entry, Padding = new Thickness(1, 0), Stroke = Colors.Gray, StrokeThickness = 1, BackgroundColor = background };
    }

    private static Grid MovementColumns()
    {
        var grid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };
        foreach (var width in MovementWidths) grid.ColumnDefinitions.Add(new ColumnDefinition(width));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); return grid;
    }

    private View MovementRow(string[] values, JournalLine line)
    {
        var row = MovementColumns(); row.HeightRequest = 16;
        row.BackgroundColor = selected == line ? Color.FromArgb("#0078D7") : Colors.Transparent;
        for (var i = 0; i < values.Length; i++)
        {
            var label = ClassicWorkspaceChrome.Text(values[i]); label.Padding = new Thickness(2, 0); label.LineBreakMode = LineBreakMode.NoWrap;
            label.TextColor = selected == line ? Colors.White : i < 2 ? Colors.Blue : Colors.Black;
            if (i >= 3) label.HorizontalTextAlignment = TextAlignment.End;
            row.Add(label, i, 0);
        }
        var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => { selected = line; Render(); }; row.GestureRecognizers.Add(tap);
        return row;
    }

    private async Task FindType()
    {
        var existing = new HashSet<string>();
        for (var m = 1; m <= 12; m++) foreach (var journal in await db.Journals(company.Id, original.Date.Year, m)) existing.Add(journal.Type);
        if (existing.Count == 0) { await DisplayAlertAsync("Tipo de póliza", "Todavía no hay tipos usados en esta contabilidad. Escriba el tipo en el campo correspondiente.", "Cerrar"); type.Focus(); return; }
        var choice = await DisplayActionSheetAsync("Tipos usados en esta contabilidad", "Cancelar", null, existing.Order().ToArray());
        if (existing.Contains(choice)) type.Text = choice;
    }

    private Task OpenModule(string key) => Guard(async () =>
    {
        if (key == "exit") { await Navigation.PopAsync(); return; }
        if (key == "accumulate") { await DisplayAlertAsync("Aviso", "Acumular Saldos no está disponible en esta versión de prueba.", "Cerrar"); return; }
        await Gl2000Chrome.Navigate(this, db, company, original.Date.Year, original.Date.Month, key);
    });
}
