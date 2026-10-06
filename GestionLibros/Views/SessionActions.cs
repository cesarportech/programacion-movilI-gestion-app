using GestionLibros.Data;

namespace GestionLibros.Views;

internal static class SessionActions
{
    // Cerrar Sesión: after confirming, deletes the remembered token from this device and from the
    // database, then returns to the login. Closing the window never comes through here.
    internal static async Task SignOut(Page page, AppDatabase db)
    {
        if (!await page.DisplayAlertAsync("Cerrar Sesión",
            "¿Cerrar la sesión? La próxima vez se pedirá usuario y contraseña. Los cambios sin aceptar en pantallas abiertas se pierden.",
            "Cerrar Sesión", "Cancelar")) return;
        SessionStore.Clear();
        try { await db.SignOut(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); db.Logout(); }
        if (page.Window != null) page.Window.Page = new LoginPage(db);
    }
}
