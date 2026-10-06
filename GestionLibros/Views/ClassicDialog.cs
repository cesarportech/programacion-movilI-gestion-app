namespace GestionLibros.Views;

// Small floating child window in the GL2000 style (title bar with close button, "General" tab,
// green form and Aceptar / Cancelar / Ayuda), drawn over its page. The full-size transparent layer
// behind it blocks clicks on the page while it is open.
internal abstract class ClassicDialog : Grid
{
    private static readonly Color DialogFace = Color.FromArgb("#ECE9D8");
    protected static readonly Color PanelGreen = Color.FromArgb("#91F4CE");
    protected static readonly Color LabelBlue = Color.FromArgb("#0000FF");
    protected readonly Page Owner;
    protected readonly AbsoluteLayout Form = new() { BackgroundColor = PanelGreen };
    protected readonly Label Error = ClassicWorkspaceChrome.Text("");
    private readonly Label caption = ClassicWorkspaceChrome.Text("");
    private bool busy;

    protected ClassicDialog(Page owner, double width, double formHeight, string helpTitle, string helpText)
    {
        Owner = owner;
        IsVisible = false;
        BackgroundColor = Color.FromArgb("#01000000");

        Error.TextColor = Color.FromArgb("#9D1010"); Error.LineBreakMode = LineBreakMode.TailTruncation;
        var tab = new Border { HorizontalOptions = LayoutOptions.Start, WidthRequest = 56, Padding = new Thickness(5, 0),
            BackgroundColor = PanelGreen, Stroke = Colors.Gray, StrokeThickness = 1, Content = ClassicWorkspaceChrome.Text("General", true) };
        var buttons = new HorizontalStackLayout { Spacing = 5, HorizontalOptions = LayoutOptions.End, Padding = new Thickness(0, 4, 2, 0) };
        buttons.Children.Add(JournalsPage.ActionButton("Aceptar", "login_accept.png", Accept, 84));
        buttons.Children.Add(JournalsPage.ActionButton("Cancelar", "login_cancel.png", () => { Close(); return Task.CompletedTask; }, 88));
        buttons.Children.Add(JournalsPage.ActionButton("Ayuda", "gl_help.png", () => owner.DisplayAlertAsync(helpTitle, helpText, "Cerrar"), 82));

        var content = new Grid { Padding = new Thickness(6, 4, 6, 6), RowSpacing = 0,
            RowDefinitions = { new RowDefinition(20), new RowDefinition(formHeight), new RowDefinition(34) } };
        content.Add(tab, 0, 0);
        content.Add(new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Content = Form }, 0, 1);
        content.Add(buttons, 0, 2);
        Add(ChildWindow(caption, content, width, Close));
    }

    // Child-window frame: gradient title bar with icon, caption and red close button, around any content.
    internal static Border ChildWindow(Label caption, View content, double width, Action close)
    {
        var titleBar = new Grid { HeightRequest = 24, Padding = new Thickness(4, 0, 3, 0), ColumnSpacing = 5,
            Background = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
                GradientStops = { new GradientStop(Color.FromArgb("#FFFFFF"), 0), new GradientStop(Color.FromArgb("#D6DFF7"), 1) } },
            ColumnDefinitions = { new ColumnDefinition(18), new ColumnDefinition(GridLength.Star), new ColumnDefinition(32) } };
        titleBar.Add(new Image { Source = "gl_catalog.png", WidthRequest = 16, HeightRequest = 16 }, 0, 0);
        caption.FontSize = 12; titleBar.Add(caption, 1, 0);
        var closeButton = new Button { Text = "✕", FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#D2553D"), CornerRadius = 2, Padding = 0, BorderWidth = 1, BorderColor = Colors.White,
            MinimumHeightRequest = 0, MinimumWidthRequest = 0, WidthRequest = 30, HeightRequest = 18 };
        closeButton.Clicked += (_, _) => close();
        SemanticProperties.SetDescription(closeButton, "Cerrar");
        titleBar.Add(closeButton, 2, 0);
        var window = new Border { WidthRequest = width, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            BackgroundColor = DialogFace, Stroke = Color.FromArgb("#4F6FBF"), StrokeThickness = 2, Padding = 0,
            Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.35f, Offset = new Point(3, 3), Radius = 6 },
            Content = new VerticalStackLayout { Spacing = 0, Children = { titleBar, content } } };
        MakeDraggable(window, titleBar);
        return window;
    }

    // Lets the user move the window by dragging its title bar, kept inside the area that hosts it.
    // The press starts on the title bar; moves and release are tracked on the host so a fast drag
    // that leaves the title bar keeps following the mouse. The host needs a hit-testable background.
    private static void MakeDraggable(Border window, View titleBar)
    {
        var dragging = false;
        Point start = default;
        double baseX = 0, baseY = 0;
        View? Host() => window.Parent as View;

        var press = new PointerGestureRecognizer();
        press.PointerPressed += (_, e) =>
        {
            if (Host() is not { } host || e.GetPosition(host) is not { } position) return;
            dragging = true; start = position; baseX = window.TranslationX; baseY = window.TranslationY;
        };
        titleBar.GestureRecognizers.Add(press);

        var track = new PointerGestureRecognizer();
        track.PointerMoved += (_, e) =>
        {
            if (!dragging || Host() is not { } host || e.GetPosition(host) is not { } position) return;
            var limitX = Math.Max(0, (host.Width - window.Width) / 2);
            var limitY = Math.Max(0, (host.Height - window.Height) / 2);
            window.TranslationX = Math.Clamp(baseX + position.X - start.X, -limitX, limitX);
            window.TranslationY = Math.Clamp(baseY + position.Y - start.Y, -limitY, limitY);
        };
        track.PointerReleased += (_, _) => dragging = false;
        track.PointerExited += (_, _) => dragging = false;
        window.ParentChanged += (_, _) => { if (Host() is { } host && !host.GestureRecognizers.Contains(track)) host.GestureRecognizers.Add(track); };
    }

    protected void Put(View view, double x, double y, double width, double height)
    {
        AbsoluteLayout.SetLayoutBounds(view, new Rect(x, y, width, height));
        Form.Children.Add(view);
    }

    protected Label FieldLabel(string text, double y)
    {
        var label = ClassicWorkspaceChrome.Text(text, true); label.TextColor = LabelBlue;
        Put(label, 8, y, 110, 20);
        return label;
    }

    protected void Open(string title, Entry focus)
    {
        caption.Text = title; Error.Text = "";
        // Each time it opens, the window starts centered again.
        if (Children.FirstOrDefault() is View window) window.TranslationX = window.TranslationY = 0;
        IsVisible = true;
        Dispatcher.Dispatch(() => focus.Focus());
    }

    protected void Close() { if (!busy) IsVisible = false; }

    // Validates and saves; returns false (keeping the dialog open) when the data is not accepted.
    protected abstract Task<bool> Save();

    protected async Task Accept()
    {
        if (busy) return;
        busy = true; Error.Text = "";
        try { if (await Save()) { busy = false; Close(); } }
        catch (InvalidOperationException ex) { Error.Text = ex.Message; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); Error.Text = "No se pudo completar la operación. Inténtelo nuevamente."; }
        finally { busy = false; }
    }

    protected static Border Field(Entry entry, Color background, string name)
    {
        entry.FontFamily = "Arial"; entry.FontSize = 11; entry.FontAutoScalingEnabled = false;
        entry.TextColor = Colors.Black; entry.BackgroundColor = background; entry.MinimumHeightRequest = 0;
        entry.MinimumWidthRequest = 0; entry.HeightRequest = 18; entry.Margin = 0;
        entry.IsTextPredictionEnabled = false; entry.IsSpellCheckEnabled = false;
        SemanticProperties.SetDescription(entry, name);
        ClassicWorkspaceChrome.CompactEntry(entry);
        return new Border { Content = entry, Padding = 0, Stroke = Colors.Gray, StrokeThickness = 1, BackgroundColor = background };
    }
}
