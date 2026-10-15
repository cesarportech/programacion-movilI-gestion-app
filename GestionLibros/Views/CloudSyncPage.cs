using GestionLibros.Data;

namespace GestionLibros.Views;

internal sealed class CloudSyncDialog : Grid
{
    private readonly AppDatabase db;
    private readonly Entry password = new() { IsPassword = true, Placeholder = "Contraseña de su usuario", MaxLength = 100 };
    private readonly Label status = new() { LineBreakMode = LineBreakMode.WordWrap, TextColor = Colors.Black };
    private readonly View send;
    private readonly Entry api = new() { Text = "", IsSpellCheckEnabled = false };
    private readonly View configure;
    private readonly Page owner;
    private CancellationTokenSource? cancellation;
    private bool closeRequested;
    private bool busy;
    private static string ConfigPath => Path.Combine(FileSystem.AppDataDirectory, "fredi-api.json");
    public CloudSyncDialog(Page owner, AppDatabase db)
    {
        this.db = db; this.owner = owner; BackgroundColor = Color.FromArgb("#01000000");
        send = JournalsPage.ActionButton("Acumular", "gl_accumulate.png", Upload, 100);
        configure = JournalsPage.ActionButton("Guardar API", "login_accept.png", Configure, 110);
        var master = db.ValidateSession().IsMaster;
        api.Text = CloudConfiguration.Load(ConfigPath).BaseUrl;
        api.IsVisible = configure.IsVisible = master;
        password.FontFamily = api.FontFamily = "Arial";
        password.FontSize = api.FontSize = 11;
        password.TextColor = api.TextColor = Colors.Black;
        password.BackgroundColor = api.BackgroundColor = Colors.White;
        password.HeightRequest = api.HeightRequest = 22;
        password.MinimumHeightRequest = api.MinimumHeightRequest = 0;
        ClassicWorkspaceChrome.CompactEntry(password); ClassicWorkspaceChrome.CompactEntry(api);
        var form = new VerticalStackLayout { Padding = 12, Spacing = 10, BackgroundColor = Color.FromArgb("#91F4CE") };
        Label Text(string text) { var label = ClassicWorkspaceChrome.Text(text); label.LineBreakMode = LineBreakMode.WordWrap; return label; }
        form.Children.Add(Text("Envíe los cambios guardados a la nube de pruebas. Sin conexión, permanecerán pendientes en este equipo."));
        form.Children.Add(Text("Los borradores se respaldan por separado y no afectan los saldos. Esta operación no descarga cambios de otros equipos."));
        form.Children.Add(Text($"Usuario: {db.Session?.Username}"));
        var fields = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new(95), new(GridLength.Star) } };
        var passwordLabel = ClassicWorkspaceChrome.Text("Contraseña:", true); passwordLabel.TextColor = Colors.Blue;
        fields.Add(passwordLabel, 0, 0);
        fields.Add(new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Content = password }, 1, 0);
        form.Children.Add(fields);
        status.FontFamily = "Arial"; status.FontSize = 11;
        form.Children.Add(status);
        if (master)
        {
            form.Children.Add(Text("Dirección base de la API:"));
            form.Children.Add(new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Content = api });
            configure.HorizontalOptions = LayoutOptions.End; form.Children.Add(configure);
        }
        var tab = new Border { HorizontalOptions = LayoutOptions.Start, BackgroundColor = Color.FromArgb("#91F4CE"),
            Stroke = Colors.Gray, Padding = new Thickness(6, 2), Content = ClassicWorkspaceChrome.Text("General", true) };
        var buttons = new HorizontalStackLayout { Spacing = 5, HorizontalOptions = LayoutOptions.End,
            Children = { send, JournalsPage.ActionButton("Cancelar", "login_cancel.png", RequestClose, 90),
                JournalsPage.ActionButton("Ayuda", "gl_help.png", () => owner.DisplayAlertAsync("Acumular", "Acumular envía los cambios guardados. Cancelar detiene los envíos siguientes y vuelve a la pantalla anterior. Un envío ya confirmado permanece en la nube; uno sin confirmar se conserva para reintentarlo sin duplicarlo.", "Cerrar"), 82) } };
        var body = new Grid { Padding = 6, RowSpacing = 5, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) } };
        body.Add(tab, 0, 0);
        body.Add(new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Content = new ScrollView { Content = form } }, 0, 1);
        body.Add(buttons, 0, 2);
        var window = ClassicDialog.ChildWindow(ClassicWorkspaceChrome.Text("Acumular · nube de pruebas"), body, 580, () => _ = RequestClose());
        Add(window);
        SizeChanged += (_, _) =>
        {
            if (Width > 0) window.WidthRequest = Math.Min(580, Math.Max(280, Width - 16));
            if (Height > 0) body.HeightRequest = Math.Min(master ? 390 : 300, Math.Max(150, Height - 50));
        };
        Loaded += async (_, _) => { try { await Refresh(); password.Focus(); } catch (InvalidOperationException ex) { status.Text = ex.Message; } };
        Unloaded += (_, _) => cancellation?.Cancel();
    }
    private async Task Configure()
    {
        if (busy) return;
        try
        {
            if (!db.ValidateSession(true).IsMaster) return;
            var config = new CloudConfiguration(api.Text ?? ""); config.Validate();
            if (config.BaseUrl != CloudConfiguration.Load(ConfigPath).BaseUrl &&
                !await owner.DisplayAlertAsync("Cambiar servidor", "La dirección recibirá los datos y las credenciales al acumular. El nuevo servidor debe tener el esquema FREDI y las revisiones de este conjunto de datos; cambiar la URL no migra la nube anterior.", "Cambiar", "Cancelar")) return;
            await File.WriteAllTextAsync(ConfigPath + ".tmp", System.Text.Json.JsonSerializer.Serialize(config));
            File.Move(ConfigPath + ".tmp", ConfigPath, true); status.Text = "Dirección API guardada.";
        }
        catch (Exception ex) { status.Text = ex is InvalidOperationException ? ex.Message : "No se pudo guardar la configuración."; }
    }
    private Task RequestClose()
    {
        closeRequested = true;
        if (busy) { status.Text = "Cancelando… Los envíos sin confirmar quedarán pendientes."; cancellation?.Cancel(); }
        else Close();
        return Task.CompletedTask;
    }
    private void Close()
    {
        password.Text = "";
        if (Parent is Grid root) root.Remove(this);
    }
    private async Task Refresh()
    {
        var state = await db.CloudSyncStatus();
        status.Text = $"Grupos pendientes: {state.PendingScopes}. Último envío: {(state.LastUpload?.ToLocalTime().ToString("g") ?? "ninguno")}.";
    }
    private async Task Upload()
    {
        if (busy) return;
        busy = true; send.IsEnabled = configure.IsEnabled = password.IsEnabled = api.IsEnabled = false;
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        CloudApi? client = null;
        try
        {
            client = new CloudApi(CloudConfiguration.Load(ConfigPath));
            var user = db.ValidateSession(true);
            var pending = await db.PrepareCloudUploads();
            if (pending.Count == 0) { status.Text = "Todos los cambios guardados ya están confirmados en la nube."; return; }
            if (string.IsNullOrEmpty(password.Text)) { status.Text = "Escriba su contraseña para autorizar el envío."; return; }
            status.Text = "Conectando con la nube…";
            token.ThrowIfCancellationRequested();
            await client.Login(await db.CloudDataset(), user.Username, password.Text, token); password.Text = "";
            var completed = 0;
            // Drain a retry first, then any newer changes that could not be included in its frozen payload.
            for (var round = 0; round < 5 && pending.Count > 0; round++)
            {
                foreach (var item in pending)
                {
                    token.ThrowIfCancellationRequested();
                    if (db.ValidateSession(true) != user) throw new InvalidOperationException("La sesión cambió. El resto queda pendiente.");
                    status.Text = $"Enviando… {completed} grupos confirmados.";
                    var revision = await client.Upload(item, token);
                    await db.AcknowledgeCloudUpload(item.Scope, item.RequestId, revision); completed++;
                }
                pending = await db.PrepareCloudUploads();
            }
            await Refresh(); status.Text = $"Acumulación completada: {completed} grupos confirmados. " + status.Text;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { status.Text = "Envío cancelado. Los cambios sin confirmar siguen pendientes."; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { status.Text = "No se pudo confirmar la conexión. Sus datos siguen guardados localmente y los envíos sin confirmar quedan pendientes. Vuelva a Acumular cuando tenga conexión."; }
        catch (InvalidOperationException ex) { status.Text = ex.Message; }
        catch { status.Text = "No se pudo completar la sincronización. Se conservan los envíos pendientes."; }
        finally { password.Text = ""; if (client != null) { await client.Logout(); client.Dispose(); } cancellation.Dispose(); cancellation = null; busy = false; send.IsEnabled = configure.IsEnabled = password.IsEnabled = api.IsEnabled = true; if (closeRequested) Close(); }
    }
}
