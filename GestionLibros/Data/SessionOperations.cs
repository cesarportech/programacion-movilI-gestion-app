using System.Security.Cryptography;
using System.Text;
using GestionLibros.Models;

namespace GestionLibros.Data;

// Remembered sessions: closing the window keeps the user signed in; only signing out ends it.
public sealed partial class AppDatabase
{
    private string? tokenHash;

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // Creates a token for the signed-in user. The caller keeps the token on the device; the database
    // only keeps its hash, so reading the database file is not enough to sign in.
    public async Task<string> IssueToken()
    {
        var user = RequireSession();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = DateTime.Now;
        await db.InsertAsync(new SessionToken { UserId = user.UserId, TokenHash = Hash(token), CreatedAt = now, LastUsedAt = now });
        tokenHash = Hash(token);
        return token;
    }

    // Restores the session of a token issued earlier. Returns false (and stays signed out) when the
    // token was revoked, belongs to a deleted user or is not valid.
    public async Task<bool> Resume(string? token)
    {
        session = null; tokenHash = null;
        await Initialize();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return false;
        var hash = Hash(token);
        var stored = await db.Table<SessionToken>().Where(t => t.TokenHash == hash).FirstOrDefaultAsync();
        if (stored == null) return false;
        var user = await db.FindAsync<LocalUser>(stored.UserId);
        if (user == null) { await db.DeleteAsync(stored); return false; }
        stored.LastUsedAt = DateTime.Now;
        await db.UpdateAsync(stored);
        session = new(user.Id, user.Username, user.IsMaster, user.CompanyId);
        tokenHash = hash;
        return true;
    }

    // Password recovery for someone with access to this computer (the database file lives in the
    // Windows profile). Only the local tool Tools/RestablecerContrasena calls it; the app never does.
    // It also revokes the user's remembered sessions.
    public async Task ResetPassword(string username, string newPassword)
    {
        await Initialize();
        username = username.Trim().ToLowerInvariant();
        var user = await db.Table<LocalUser>().Where(u => u.Username == username).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("El usuario no existe.");
        var replacement = NewUser(username, newPassword, user.IsMaster, user.CompanyId);
        user.Salt = replacement.Salt;
        user.PasswordHash = replacement.PasswordHash;
        await db.RunInTransactionAsync(c =>
        {
            c.Update(user);
            c.Execute("DELETE FROM SessionToken WHERE UserId = ?", user.Id);
        });
    }

    // Cerrar sesión: revokes this device's token and clears the session.
    public async Task SignOut()
    {
        var hash = tokenHash;
        Logout();
        if (hash == null) return;
        await Initialize();
        await db.ExecuteAsync("DELETE FROM SessionToken WHERE TokenHash = ?", hash);
    }
}
