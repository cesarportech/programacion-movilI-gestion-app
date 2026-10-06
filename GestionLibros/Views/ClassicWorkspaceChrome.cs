using GestionLibros.Data;

namespace GestionLibros.Views;

// Compact desktop frame. Routes still use the existing accounting pages and session.
internal static class ClassicWorkspaceChrome
{
    internal static Label Text(string text, bool bold = false) => new()
    {
        Text = text, FontFamily = "Arial", FontSize = 11, FontAutoScalingEnabled = false,
        FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
        TextColor = Colors.Black, VerticalTextAlignment = TextAlignment.Center
    };

    internal static Brush ToolbarBrush() => new LinearGradientBrush
    {
        StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
        GradientStops = { new GradientStop(Color.FromArgb("#D3D3D3"), 0),
            new GradientStop(Color.FromArgb("#A5A5A5"), 0.72f), new GradientStop(Color.FromArgb("#D8D8D8"), 1) }
    };

    private static Button PlainButton(string text) => new()
    {
        Text = text, FontFamily = "Arial", FontSize = 11, FontAutoScalingEnabled = false,
        TextColor = Colors.Black, BackgroundColor = Colors.Transparent, BorderWidth = 0,
        CornerRadius = 0, Padding = 0, MinimumWidthRequest = 0, MinimumHeightRequest = 0
    };

    internal static Button Tool(string text, string image, Func<Task> action)
    {
        var button = PlainButton(text);
        button.ImageSource = image;
        button.FontAttributes = FontAttributes.Bold;
        button.WidthRequest = 64;
        button.HeightRequest = 50;
        button.ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Top, 0);
        AutomationProperties.SetName(button, text);
        ConfigureStates(button);
        button.Clicked += async (_, _) =>
        {
            if (!button.IsEnabled) return;
            button.IsEnabled = false;
            try { await action(); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private static void ConfigureStates(Button button)
    {
        var states = new VisualStateGroup { Name = "CommonStates" };
        foreach (var (name, color, offset) in new[] {
            ("Normal", Colors.Transparent, 0d), ("PointerOver", Color.FromArgb("#30FFFFFF"), 0d),
            ("Pressed", Color.FromArgb("#40000000"), 1d), ("Disabled", Colors.Transparent, 0d) })
        {
            var state = new VisualState { Name = name };
            state.Setters.Add(new Setter { Property = VisualElement.BackgroundColorProperty, Value = color });
            state.Setters.Add(new Setter { Property = VisualElement.TranslationYProperty, Value = offset });
            states.States.Add(state);
        }
        VisualStateManager.SetVisualStateGroups(button, new VisualStateGroupList { states });
    }

    internal static View Menu(Page page, IEnumerable<MenuBarItem> menus)
    {
        var row = new HorizontalStackLayout { Spacing = 0, BackgroundColor = Colors.White };
        foreach (var menu in menus)
        {
            var button = PlainButton(menu.Text);
            button.Padding = new Thickness(7, 0);
            button.HeightRequest = 20;
            ConfigureStates(button);
            button.Clicked += async (_, _) =>
            {
#if WINDOWS
                if (button.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement anchor)
                {
                    var popup = new Microsoft.UI.Xaml.Controls.MenuFlyout();
                    foreach (var item in menu) popup.Items.Add(NativeItem(item));
                    popup.ShowAt(anchor, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                    { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft });
                    return;
                }
#endif
                await ShowMenu(page, menu.Text, menu);
            };
            row.Children.Add(button);
        }
        return new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            BackgroundColor = Colors.White, Content = row };
    }

#if WINDOWS
    private static Microsoft.UI.Xaml.Controls.MenuFlyoutItemBase NativeItem(Microsoft.Maui.IMenuElement item)
    {
        if (item is MenuFlyoutSubItem group)
        {
            var submenu = new Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem { Text = group.Text, FontSize = 12 };
            foreach (var child in group) submenu.Items.Add(NativeItem(child));
            return submenu;
        }
        if (item is MenuFlyoutItem action)
            return new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = action.Text,
                Command = action.Command, CommandParameter = action.CommandParameter, IsEnabled = action.IsEnabled,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Arial"), FontSize = 12 };
        return new Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator();
    }
#endif

    private static async Task ShowMenu(Page page, string title, IEnumerable<Microsoft.Maui.IMenuElement> items)
    {
        var choices = items.Where(x => x is MenuFlyoutSubItem || x is MenuFlyoutItem { IsEnabled: true }).ToArray();
        var names = choices.Select(x => x is MenuFlyoutSubItem group ? group.Text : ((MenuFlyoutItem)x).Text).ToArray();
        var selected = await page.DisplayActionSheetAsync(title, "Cancelar", null, names);
        var index = Array.IndexOf(names, selected);
        if (index < 0) return;
        if (choices[index] is MenuFlyoutSubItem submenu) await ShowMenu(page, submenu.Text, submenu);
        else if (choices[index] is MenuFlyoutItem action && action.Command?.CanExecute(action.CommandParameter) == true)
            action.Command.Execute(action.CommandParameter);
    }

    internal static View Status(AppDatabase db, string hint = "")
    {
        var row = new Grid { HeightRequest = 21, Padding = new Thickness(8, 3, 8, 2), ColumnSpacing = 8,
            BackgroundColor = Color.FromArgb("#F0F0F0"),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(120),
                new ColumnDefinition(180), new ColumnDefinition(68) } };
        View Panel(string text) => new Border { Stroke = Color.FromArgb("#A0A0A0"), StrokeThickness = 1,
            Padding = new Thickness(2, 0), Content = Text(text) };
        row.Add(Panel(hint), 0, 0);
        row.Add(Panel($"Nivel : {(db.Session?.IsMaster == true ? "Maestro" : "Operador")}"), 1, 0);
        row.Add(Panel($"Usuario : {db.Session?.Username}"), 2, 0);
        row.Add(Panel(""), 3, 0);
        return row;
    }

    internal static void CompactPeriod(Picker months, Entry year)
    {
#if WINDOWS
        months.HandlerChanged += (_, _) =>
        {
            if (months.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ComboBox box)
            {
                box.MinHeight = box.MinWidth = 0;
                box.Padding = new Microsoft.UI.Xaml.Thickness(2, 0, 0, 0);
                box.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(0);
            }
        };
        year.HandlerChanged += (_, _) =>
        {
            if (year.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox box)
            {
                box.MinHeight = box.MinWidth = 0;
                box.Padding = new Microsoft.UI.Xaml.Thickness(0);
                box.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
                box.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(0);
            }
        };
#endif
    }

    internal static void WindowTitle(Window window, string title)
    {
        window.Title = title;
#if WINDOWS
        window.TitleBar = null;
        if (window.Handler?.PlatformView is Microsoft.Maui.MauiWinUIWindow native)
        {
            native.SetTitleBar(null);
            native.ExtendsContentIntoTitleBar = false;
            native.AppWindow.TitleBar.ExtendsContentIntoTitleBar = false;
            if (native.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                presenter.SetBorderAndTitleBar(true, true);
                presenter.IsResizable = presenter.IsMinimizable = presenter.IsMaximizable = true;
            }
            var caption = native.AppWindow.TitleBar;
            caption.BackgroundColor = caption.ButtonBackgroundColor = Microsoft.UI.Colors.White;
            caption.ForegroundColor = caption.ButtonForegroundColor = Microsoft.UI.Colors.Black;
            caption.InactiveBackgroundColor = caption.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.White;
            caption.InactiveForegroundColor = caption.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Gray;
        }
#endif
    }
}
