using GestionLibros.Data;

namespace GestionLibros.Views;

// Resume before constructing the compact login so returning users never see it flash onscreen.
internal sealed class StartupPage : ContentPage
{
    private readonly AppDatabase database;
    private readonly CloudLoadingOverlay loading;
    private readonly Label status = ClassicWorkspaceChrome.Text("Verificando la sesión guardada…");
    private readonly CancellationTokenSource lifetime = new();
    private bool started;

    internal StartupPage(AppDatabase database)
    {
        this.database = database;
        Title = "FREDI Contabilidad";
        BackgroundColor = Color.FromArgb("#ECE9D8");
        loading = new CloudLoadingOverlay(status);
        Content = loading;
        Loaded += async (_, _) => await Initialize();
        Unloaded += (_, _) => { lifetime.Cancel(); loading.Stop(); };
    }

    private async Task Initialize()
    {
        if (started || Window is not { } window) return;
        started = true;
        loading.Start("FREDI Contabilidad");
        // Brief presentation runs concurrently with session validation, never a simulated percentage.
        var presentation = Task.Delay(1000, lifetime.Token);
        var resumed = false;
        string? reason = null;
        try
        {
            var token = await SessionStore.Load();
            lifetime.Token.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(token))
            {
                resumed = await database.Resume(token);
                if (!resumed) SessionStore.Clear();
            }
            status.Text = resumed ? "Preparando su espacio de trabajo…" : "Preparando el inicio de sesión…";
            await presentation;
            lifetime.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(window.Page, this)) return;
            if (resumed)
            {
                var user = database.ValidateSession();
                window.Page = new NavigationPage(user.IsMaster ? new AdminPage(database) : new WorkspacePage(database));
                LoginWindowLayout.Restore(window);
                return;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
        catch
        {
            database.Logout(); SessionStore.Clear();
            reason = "No se pudo recuperar la sesión guardada. Inicie sesión nuevamente.";
        }
        finally { loading.Stop(); }

        if (!lifetime.IsCancellationRequested && ReferenceEquals(window.Page, this))
            window.Page = new LoginPage(database, reason);
    }
}
