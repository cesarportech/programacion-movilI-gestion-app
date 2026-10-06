namespace GestionLibros.Data;

// Keeps the session token on this device with the platform's secure storage (on Windows it is
// encrypted for the current Windows user). Failures only mean the session is not remembered.
public static class SessionStore
{
    private const string Key = "fredi.session.token";

    public static async Task<string?> Load()
    {
        try { return await SecureStorage.Default.GetAsync(Key); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return null; }
    }

    public static async Task Save(string token)
    {
        try { await SecureStorage.Default.SetAsync(Key, token); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    public static void Clear()
    {
        try { SecureStorage.Default.Remove(Key); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }
}
