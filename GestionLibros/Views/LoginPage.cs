using GestionLibros.Data;

namespace GestionLibros.Views;

public class LoginPage : ContentPage
{
    private static readonly Color DialogBlue = Color.FromRgb(0, 128, 192);
    private readonly AppDatabase database;
    private readonly Entry username = Field("Username", false);
    private readonly Entry password = Field("Password", true);
    private readonly Entry confirm = Field("ConfirmPassword", true);
    private readonly Label hint = Text("Ingrese su usuario y contraseña", 11);
    private readonly Label message = Text("", 11);
    private readonly Label downloadStatus = Text("", 11);
    private readonly CloudLoadingOverlay loading;
    private readonly AbsoluteLayout form = new() { BackgroundColor = DialogBlue };
    private readonly VerticalStackLayout dialog = new() { Spacing = 0, WidthRequest = 262 };
    private readonly Button submit = Action("Ok", "login_accept.png");
    private readonly Button cancel = Action("Cancela", "login_cancel.png");
    private readonly Label confirmLabel = Text("Confirmar:", 11);
    private readonly Border confirmBox;
    private bool setup;
    private bool busy;
    private bool ready;

    public LoginPage(AppDatabase database, string? reason = null)
    {
        this.database = database;
        Title = "Iniciar Sesion:";
        BackgroundColor = DialogBlue;
        var caption = new Grid { HeightRequest = 25, BackgroundColor = Colors.White };
        caption.Children.Add(new Label { Text = "Iniciar Sesion:", FontFamily = "Arial", FontSize = 11,
            TextColor = Colors.Black, Margin = new Thickness(3, 0), VerticalTextAlignment = TextAlignment.Center });
        dialog.Children.Add(caption);
        Put(Text("Sistema de Contabilidad", 16), 20, 3, 230, 24);
        Put(Text("Usuario:", 11), 33, 32, 76, 20);
        Put(Box(username), 111, 31, 130, 20);
        Put(Text("Contraseña:", 11), 33, 58, 76, 20);
        Put(Box(password), 111, 57, 130, 20);
        Put(confirmLabel, 33, 84, 76, 20);
        confirmBox = Box(confirm);
        Put(confirmBox, 111, 83, 130, 20);
        Put(hint, 16, 81, 230, 23);
        Put(cancel, 36, 107, 82, 30);
        Put(submit, 150, 107, 72, 30);
        dialog.Children.Add(form);
        message.BackgroundColor = Colors.White;
        message.TextColor = Color.FromArgb("#9D1010");
        message.Padding = new Thickness(6);
        message.IsVisible = false;
        dialog.Children.Add(message);
        var border = new Border { Content = dialog, Stroke = Color.FromArgb("#004A72"), StrokeThickness = 1,
            BackgroundColor = DialogBlue, Padding = 0, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        loading = new CloudLoadingOverlay(downloadStatus);
        Content = new Grid { Children = { border, loading } };
        SemanticProperties.SetDescription(username, "Usuario");
        SemanticProperties.SetDescription(password, "Contraseña");
        SemanticProperties.SetDescription(confirm, "Confirmar contraseña");
        submit.IsEnabled = false;
        submit.Clicked += async (_, _) => await SignIn();
        cancel.Clicked += (_, _) => Cancel();
        username.ReturnType = ReturnType.Next;
        username.Completed += (_, _) => password.Focus();
        password.ReturnType = ReturnType.Go;
        password.Completed += async (_, _) => { if (setup) confirm.Focus(); else await SignIn(); };
        confirm.Completed += async (_, _) => await SignIn();
        Loaded += (_, _) => { if (Window != null) { LoginWindowLayout.Compact(Window, DialogHeight); username.Focus(); } };
        LayoutForm();
        if (reason != null) ShowError(reason);
    }

    private double DialogHeight => 27 + (setup ? 190 : 142) + (message.IsVisible ? 54 : 0);

    private void Put(View view, double x, double y, double width, double height)
    {
        AbsoluteLayout.SetLayoutBounds(view, new Rect(x, y, width, height));
        form.Children.Add(view);
    }

    private void LayoutForm()
    {
        confirmLabel.IsVisible = confirmBox.IsVisible = setup;
        hint.Text = setup ? "Cree el usuario maestro.\nContraseña: mínimo 10 caracteres." : "Ingrese su usuario y contraseña";
        form.HeightRequest = setup ? 190 : 142;
        AbsoluteLayout.SetLayoutBounds(hint, new Rect(16, setup ? 108 : 81, 230, setup ? 40 : 23));
        AbsoluteLayout.SetLayoutBounds(cancel, new Rect(36, setup ? 155 : 107, 82, 30));
        AbsoluteLayout.SetLayoutBounds(submit, new Rect(150, setup ? 155 : 107, 72, 30));
        if (Window != null) LoginWindowLayout.Compact(Window, DialogHeight);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            // StartupPage alone resumes remembered tokens. Logout/revocation always shows login.
            // An empty installation still asks for the user's credentials first: a client's data comes from
            // the cloud. Creating a new master is offered only when the cloud does not know the user.
            await database.NeedsSetup();
            ready = true;
            LayoutForm();
            submit.IsEnabled = true;
        }
        catch { ShowError("No se pudo abrir la base local. Verifique el acceso a la carpeta de datos."); }
    }

    private async Task SignIn()
    {
        if (busy || !ready) return;
        busy = true;
        submit.IsEnabled = cancel.IsEnabled = username.IsEnabled = password.IsEnabled = confirm.IsEnabled = false;
        message.IsVisible = false;
        LayoutForm();
        try
        {
            if (setup)
            {
                if (password.Text != confirm.Text) throw new InvalidOperationException("Las contraseñas no coinciden.");
                await database.Setup(username.Text ?? "", password.Text ?? "");
                setup = false;
                await database.Login(username.Text ?? "", password.Text ?? "");
            }
            else if (await database.CanRestoreFromCloud() || !await database.KnowsUser(username.Text ?? "")) { if (!await SignInFromCloud()) return; }
            else await database.Login(username.Text ?? "", password.Text ?? "");
            password.Text = confirm.Text = "";
            await SessionStore.Save(await database.IssueToken());
            EnterWorkspace();
        }
        catch (InvalidOperationException ex) { ShowError(ex.Message); }
        catch { ShowError("No se pudo completar el ingreso. Inténtelo nuevamente."); }
        finally
        {
            busy = false;
            submit.IsEnabled = cancel.IsEnabled = username.IsEnabled = password.IsEnabled = confirm.IsEnabled = true;
        }
    }

    // This device has no accounting yet, or not this user: download the user's data from the cloud. A local
    // master of an empty installation may still sign in; false means the form switched to creating a new master.
    private async Task<bool> SignInFromCloud()
    {
        var user = username.Text ?? ""; var secret = password.Text ?? "";
        // The animation needs more room than the compact form; on success it stays until the workspace opens.
        downloadStatus.Text = $"Descargando las contabilidades de {user.Trim().ToLowerInvariant()}. Solo ocurre la primera vez que entra en este equipo.";
        loading.Start("Primer ingreso en este equipo");
        if (Window != null) LoginWindowLayout.Compact(Window, LoadingHeight);
        var downloaded = false;
        try
        {
            using var api = new CloudApi(CloudConfiguration.Load(CloudSyncDialog.ConfigPath));
            await database.RestoreFromCloud(api, user, secret);
            downloaded = true;
            return true;
        }
        catch (CloudAuthenticationException ex)
        {
            HideLoading();
            if (await TryLocalLogin(user, secret)) return true;
            if (!await database.NeedsSetup() || !await DisplayAlertAsync("Instalación nueva",
                "La nube no reconoce este usuario y contraseña. Si es la primera instalación de FREDI y no la de un usuario ya asignado, puede crear aquí el usuario maestro.",
                "Crear maestro", "Volver a intentar")) throw new InvalidOperationException(ex.Message);
            setup = true;
            confirm.Focus();
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            HideLoading();
            if (await TryLocalLogin(user, secret)) return true;
            throw new InvalidOperationException("Los datos de este usuario todavía no están en este equipo. Conéctese a internet para descargarlos en su primer ingreso.");
        }
        finally { if (!downloaded) HideLoading(); }
    }

    private const double LoadingHeight = 330;

    private void HideLoading()
    {
        loading.Stop();
        LayoutForm();
    }

    private async Task<bool> TryLocalLogin(string user, string secret)
    {
        try { await database.Login(user, secret); return true; }
        catch (InvalidOperationException) { return false; }
    }

    private void EnterWorkspace()
    {
        var window = Window!;
        // The master administers companies and users; every other user goes straight to their accounting.
        window.Page = new NavigationPage(database.Session?.IsMaster == true ? new AdminPage(database) : new WorkspacePage(database));
        LoginWindowLayout.Restore(window);
    }

    private void ShowError(string text)
    {
        loading.Stop();
        message.Text = text;
        message.HeightRequest = 54;
        message.IsVisible = true;
        LayoutForm();
    }

    private void Cancel()
    {
        if (busy) return;
        password.Text = confirm.Text = "";
#if WINDOWS || MACCATALYST
        _ = PendingEdits.CloseWindow(Window);
#else
        username.Text = "";
        message.IsVisible = false;
        LayoutForm();
#endif
    }

    private static Label Text(string text, double size) => new()
    {
        Text = text, FontFamily = "Arial", FontSize = size, TextColor = Colors.Black,
        FontAutoScalingEnabled = false, VerticalTextAlignment = TextAlignment.Center
    };

    private static Border Box(Entry entry) => new()
    {
        Content = entry, BackgroundColor = Colors.White, Stroke = Colors.LightGray,
        StrokeThickness = 1, Padding = 0
    };

    private static Entry Field(string id, bool secret)
    {
        var entry = new Entry { AutomationId = id, IsPassword = secret, FontFamily = "Arial", FontSize = 11,
            FontAutoScalingEnabled = false, TextColor = Colors.Black, BackgroundColor = Colors.White,
            MinimumHeightRequest = 0, MinimumWidthRequest = 0, HeightRequest = 18,
            Margin = 0, IsTextPredictionEnabled = false, IsSpellCheckEnabled = false };
        ClassicWorkspaceChrome.CompactEntry(entry);
        return entry;
    }

    private static Button Action(string text, string image)
    {
        var button = new Button { Text = text, FontFamily = "Arial", FontSize = 11,
            FontAutoScalingEnabled = false, TextColor = Colors.Black, BackgroundColor = Colors.Transparent,
            BorderWidth = 0, CornerRadius = 0, Padding = 0, MinimumHeightRequest = 0, MinimumWidthRequest = 0,
            ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 7) };
        button.ImageSource = image;
        button.Pressed += (_, _) => AnimatePress(button, true);
        button.Released += (_, _) => AnimatePress(button, false);
        button.Unfocused += (_, _) => AnimatePress(button, false);
        button.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsEnabled) && !button.IsEnabled)
                AnimatePress(button, false);
        };
        return button;
    }

    private static void AnimatePress(Button button, bool pressed)
    {
        button.AbortAnimation("LoginPress");
        var initialScale = button.Scale;
        var initialOffset = button.TranslationY;
        var targetScale = pressed ? 0.92 : 1;
        var targetOffset = pressed ? 2 : 0;
        new Animation(progress =>
        {
            button.Scale = initialScale + (targetScale - initialScale) * progress;
            button.TranslationY = initialOffset + (targetOffset - initialOffset) * progress;
        }).Commit(button, "LoginPress", length: pressed ? 70u : 120u, easing: Easing.CubicOut);
    }
}
