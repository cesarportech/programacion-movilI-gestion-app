using GestionLibros.Data;
using GestionLibros.Views;
namespace GestionLibros;
public partial class App : Application
{
 internal AppDatabase Database { get; }
 public App(AppDatabase database)
 {
  InitializeComponent(); UserAppTheme = AppTheme.Light; Database = database;
  database.SessionInvalidated += (_, reason) => Dispatcher.Dispatch(() =>
  {
   SessionStore.Clear();
   foreach (var window in Windows) window.Page = new LoginPage(database, reason + " Los borradores guardados se podrán recuperar al volver a ingresar.");
  });
 }
 protected override Window CreateWindow(IActivationState? activationState)
 {
  var window = new Window(new LoginPage(Database));
  var timer = Dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(5);
  void Check(bool activity = false)
  {
   if (Database.Session == null) return;
   try { Database.ValidateSession(activity); }
   catch (InvalidOperationException) { /* Revocation event returns the window to login. */ }
   catch (Exception ex)
   {
    System.Diagnostics.Debug.WriteLine(ex);
    Database.Logout(); SessionStore.Clear();
    window.Page = new LoginPage(Database, "No se pudo verificar la sesión. Inicie sesión nuevamente.");
   }
  }
  timer.Tick += (_, _) => Check();
  window.Activated += (_, _) => Check();
  window.Created += (_, _) =>
  {
#if WINDOWS
   if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window closeWindow)
   {
    bool allowClose = false, asking = false;
    closeWindow.AppWindow.Closing += async (_, args) =>
    {
     if (allowClose) return;
     args.Cancel = true;
     if (asking) return;
     asking = true;
     try
     {
      if (await PendingEdits.Confirm(window)) { allowClose = true; Application.Current?.CloseWindow(window); }
     }
     finally { asking = false; }
    };
   }
   // Handled events included: typing and clicks in child controls renew activity, never the timer.
   if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native && native.Content is Microsoft.UI.Xaml.UIElement root)
   {
    root.AddHandler(Microsoft.UI.Xaml.UIElement.PointerPressedEvent,
     new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => Check(true)), true);
    root.AddHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent,
     new Microsoft.UI.Xaml.Input.KeyEventHandler((_, _) => Check(true)), true);
   }
#endif
   timer.Start();
  };
  window.Destroying += (_, _) => timer.Stop();
  return window;
 }
}
