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
  var window = new Window(new StartupPage(Database));
  var timer = Dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(5);
  var checking = 0;
  // The session check reads (and occasionally writes) SQLite, so it runs off the UI thread and never
  // overlaps itself. Revocation is still noticed within 5 s; SessionInvalidated returns to login.
  void Check()
  {
   if (Database.Session == null || Interlocked.Exchange(ref checking, 1) == 1) return;
   _ = Task.Run(() =>
   {
    try { Database.ValidateSession(false); }
    catch (InvalidOperationException) { /* Revocation event returns the window to login. */ }
    catch (Exception ex)
    {
     System.Diagnostics.Debug.WriteLine(ex);
     Dispatcher.Dispatch(() =>
     {
      Database.Logout(); SessionStore.Clear();
      window.Page = new LoginPage(Database, "No se pudo verificar la sesión. Inicie sesión nuevamente.");
     });
    }
    finally { Interlocked.Exchange(ref checking, 0); }
   });
  }
  timer.Tick += (_, _) => Check();
  window.Activated += (_, _) => Check();
  window.Created += (_, _) =>
  {
#if WINDOWS
   if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window closeWindow)
   {
    closeWindow.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "fredi.ico"));
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
   // Only an in-memory timestamp is updated here: no database access on the input path.
   if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native && native.Content is Microsoft.UI.Xaml.UIElement root)
   {
    root.AddHandler(Microsoft.UI.Xaml.UIElement.PointerPressedEvent,
     new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => Database.NoteActivity()), true);
    root.AddHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent,
     new Microsoft.UI.Xaml.Input.KeyEventHandler((_, _) => Database.NoteActivity()), true);
   }
#endif
   timer.Start();
  };
  window.Destroying += (_, _) => timer.Stop();
  return window;
 }
}
