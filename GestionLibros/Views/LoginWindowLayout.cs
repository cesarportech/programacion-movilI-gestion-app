namespace GestionLibros.Views;

// Only the authentication window is compact; restore normal desktop dimensions after login.
internal static class LoginWindowLayout
{
#if WINDOWS
    private sealed class WindowState
    {
        public double Width = 1200;
        public double Height = 800;
        public bool Captured;
    }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, WindowState> States = new();
#endif

    internal static void Compact(Window window, double height)
    {
#if WINDOWS
        if (window.Handler?.PlatformView is not Microsoft.Maui.MauiWinUIWindow native) return;
        var state = States.GetOrCreateValue(window);
        if (!state.Captured)
        {
            if (window.Width > 500) state.Width = window.Width;
            if (window.Height > 400) state.Height = window.Height;
            state.Captured = true;
        }
        window.Title = "Iniciar Sesion:";
        window.TitleBar = null;
        if (native.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.Restore();
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = false;
        }
        window.MinimumWidth = 0;
        window.MinimumHeight = 0;
        window.Width = 264;
        window.Height = height;
        Center(native);
#endif
    }

#if WINDOWS
    private static void Center(Microsoft.Maui.MauiWinUIWindow native)
    {
        var display = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(native.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
        if (display != null)
        {
            var area = display.WorkArea;
            var size = native.AppWindow.Size;
            native.AppWindow.Move(new Windows.Graphics.PointInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2));
        }
    }
#endif

    internal static void Restore(Window window)
    {
#if WINDOWS
        if (window.Handler?.PlatformView is not Microsoft.Maui.MauiWinUIWindow native) return;
        if (native.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(true, true);
            presenter.IsResizable = presenter.IsMaximizable = presenter.IsMinimizable = true;
        }
        var state = States.GetOrCreateValue(window);
        window.Width = state.Width;
        window.Height = state.Height;
        window.Title = (window.Page as NavigationPage)?.CurrentPage.Title ?? "FREDI Contabilidad";
        Center(native);
        state.Captured = false;
#endif
    }
}
