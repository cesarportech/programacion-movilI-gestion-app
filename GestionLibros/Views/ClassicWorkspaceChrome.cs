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
        SemanticProperties.SetDescription(button, text);
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

    // Toolbar button that ends the session and deletes the remembered token (see SessionActions).
    internal static Button SignOutTool(Func<Task> action)
    {
        var button = Tool("Cerrar Sesión", "gl_close.png", action);
        button.WidthRequest = 84;
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

    // Icon toolbar shared by the module windows (Catálogo, Consulta…), with the read-only working period.
    internal static View ModuleToolbar(Func<string, Task> open, int year, int month)
    {
        var tools = new HorizontalStackLayout { Spacing = 1, Padding = new Thickness(3, 2, 0, 0) };
        foreach (var (caption, image, key) in new[] { ("Catálogo", "gl_catalog.png", "catalog"),
            ("Asientos", "gl_journals.png", "journals"), ("Acumular", "gl_accumulate.png", "accumulate"),
            ("Consulta", "gl_query.png", "balances"), ("Balanza", "gl_trial.png", "trial"),
            ("Saldos", "gl_balances.png", "saldos"), ("Mayor", "gl_ledger.png", "ledger"), ("Salir", "gl_exit.png", "exit") })
            tools.Children.Add(Tool(caption, image, () => open(key)));
        tools.Children.Add(SignOutTool(() => open("logout")));
        var period = new HorizontalStackLayout { Spacing = 5, Margin = new Thickness(67, 4, 0, 0), VerticalOptions = LayoutOptions.Start };
        period.Children.Add(Text("Mes", true));
        period.Children.Add(new Border { Padding = new Thickness(3, 0), Stroke = Colors.Gray, BackgroundColor = Colors.White,
            Content = Text(month.ToString("00"), true), WidthRequest = 51, HeightRequest = 18 });
        period.Children.Add(Text("Año", true)); period.Children.Add(Text(year.ToString(), true));
        tools.Children.Add(period);
        return new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Background = ToolbarBrush(),
            Content = new ScrollView { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never, Content = tools } };
    }

    // Menu row, toolbar, wallpapered body and status bar: the frame of every classic window.
    // "frame" wallpaper = the main window watermark (SISTEMAS FUSSION), used behind report dialogs.
    internal static Grid Frame(View menu, View toolbar, View body, View status, string wallpaper = "form")
    {
        var frame = new Grid { RowSpacing = 0, RowDefinitions = { new RowDefinition(20), new RowDefinition(57), new RowDefinition(GridLength.Star), new RowDefinition(21) } };
        frame.Add(menu, 0, 0); frame.Add(toolbar, 0, 1);
        var (file, width, height) = Gl2000Chrome.Texture(wallpaper);
        frame.Add(Gl2000Chrome.Backdrop(body, file, width, height), 0, 2);
        frame.Add(status, 0, 3);
        return frame;
    }

    // Removes WinUI's tall list-item padding so rows are as dense as the original grids.
    internal static void CompactRows(CollectionView table)
    {
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
    }

    // One look for every small text field: no WinUI minimum size or rounded border, and a 3 px inner
    // margin so the first and last characters never touch (or hide behind) the field edge.
    internal static void CompactEntry(Entry entry)
    {
        entry.MinimumHeightRequest = 0; entry.MinimumWidthRequest = 0; entry.Margin = 0;
        entry.FontAutoScalingEnabled = false;
#if WINDOWS
        entry.HandlerChanged += (_, _) =>
        {
            if (entry.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.TextBox box)
            {
                box.MinHeight = box.MinWidth = 0;
                box.Padding = new Microsoft.UI.Xaml.Thickness(3, 1, 3, 0);
                box.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
                box.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(0);
                box.VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center;
                box.Resources["TextControlBorderThicknessFocused"] = new Microsoft.UI.Xaml.Thickness(0);
            }
        };
#endif
    }

    internal static void CompactPicker(Picker picker)
    {
#if WINDOWS
        picker.HandlerChanged += (_, _) =>
        {
            if (picker.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ComboBox box)
            {
                box.MinHeight = box.MinWidth = 0;
                box.Padding = new Microsoft.UI.Xaml.Thickness(2, 0, 0, 0);
                box.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(0);
            }
        };
#endif
    }

    internal static void CompactPeriod(Picker months, Entry year)
    {
        CompactPicker(months);
        CompactEntry(year);
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
            // MAUI keeps a dark navigation strip (back arrow + repeated title) above every page on Windows,
            // even with HasNavigationBar off. The GL screens have no such bar, so it is collapsed.
            if (native.Content is Microsoft.UI.Xaml.DependencyObject root) CollapseToolbars(root);
        }
#endif
    }

#if WINDOWS
    private static void CollapseToolbars(Microsoft.UI.Xaml.DependencyObject parent)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            // MAUI's root NavigationView pads its content 32 px for a title bar the GL screens do not have.
            if (child is Microsoft.UI.Xaml.Controls.NavigationView navigation)
            {
                navigation.IsTitleBarAutoPaddingEnabled = false;
                navigation.IsBackButtonVisible = Microsoft.UI.Xaml.Controls.NavigationViewBackButtonVisible.Collapsed;
                navigation.IsPaneToggleButtonVisible = false;
            }
            // ...and gives the page content a 32 px top margin (the dark strip). MAUI re-applies it on layout,
            // so it is kept at zero whenever it changes.
            if (child is Microsoft.UI.Xaml.FrameworkElement { Name: "ContentGrid" } content)
            {
                content.Margin = new Microsoft.UI.Xaml.Thickness(0);
                if (content.Tag is not "fredi-no-titlebar")
                {
                    content.Tag = "fredi-no-titlebar";
                    content.RegisterPropertyChangedCallback(Microsoft.UI.Xaml.FrameworkElement.MarginProperty, (sender, _) =>
                    {
                        if (sender is Microsoft.UI.Xaml.FrameworkElement grid && grid.Margin.Top != 0) grid.Margin = new Microsoft.UI.Xaml.Thickness(0);
                    });
                }
            }
            // MauiToolbar = navigation strip; AppTitleBarContainer/AppTitleBar = MAUI's own title bar
            // (back arrow + title) drawn inside the window under the Windows title bar.
            if (child is Microsoft.UI.Xaml.FrameworkElement element &&
                (child.GetType().Name == "MauiToolbar" || element.Name is "AppTitleBarContainer" or "AppTitleBar" || element.Name.Contains("BackButton")))
            {
                element.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                // The root grid keeps a fixed-height row for the title bar; give it no height.
                if (Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element) is Microsoft.UI.Xaml.Controls.Grid grid)
                {
                    var row = Microsoft.UI.Xaml.Controls.Grid.GetRow(element);
                    if (row < grid.RowDefinitions.Count) grid.RowDefinitions[row].Height = new Microsoft.UI.Xaml.GridLength(0);
                }
            }
            else CollapseToolbars(child);
        }
    }
#endif
}
