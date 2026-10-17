using System.Security.Cryptography;
using System.Text;
using GestionLibros.Models;

namespace GestionLibros.Data;

// Remembered sessions share the same revocation, idle and absolute limits as open sessions.
public sealed partial class AppDatabase
{
    private string? tokenHash;
    private long sessionVersion;
    private DateTime sessionStartedUtc, lastActivityUtc, lastActivityPersistedUtc;
    // Guards the activity timestamps: input events record activity from the UI thread while the
    // periodic validation runs on a background thread.
    private readonly object activityGate = new();
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);
    // Activity is kept in memory on every click/key; the remembered token's LastUsedAt is written at most
    // this often, so typing no longer causes a disk write (and fsync) per keystroke.
    public static readonly TimeSpan ActivityPersistInterval = TimeSpan.FromMinutes(1);
    public event EventHandler<string>? SessionInvalidated;

    private void StartSession(LocalUser user, DateTime started, DateTime activity)
    {
        sessionVersion = user.SecurityVersion;
        lock (activityGate) { sessionStartedUtc = started; lastActivityUtc = lastActivityPersistedUtc = activity; }
        session = new(user.Id, user.Username, user.IsMaster, user.CompanyId);
    }

    // Records user activity without touching the database (called on every click and key press).
    // An already idle-expired session is not revived: the next validation still locks it.
    public void NoteActivity()
    {
        if (session == null) return;
        var now = clock.GetUtcNow().UtcDateTime;
        lock (activityGate)
            if (now >= lastActivityUtc && now - lastActivityUtc < IdleTimeout) lastActivityUtc = now;
    }

    // Background checks do not renew activity. Every protected operation checks the stored user.
    public UserSession ValidateSession(bool activity = false)
    {
        var current = session ?? throw new InvalidOperationException("Debe iniciar sesión.");
        var connection = db.GetConnection();
        using (connection.Lock())
        {
            var now = clock.GetUtcNow().UtcDateTime;
            DateTime lastActivityUtc;
            lock (activityGate) lastActivityUtc = this.lastActivityUtc;
            var user = connection.Find<LocalUser>(current.UserId);
            var token = tokenHash == null ? null : connection.Table<SessionToken>().FirstOrDefault(t => t.TokenHash == tokenHash);
            var reason = user == null || user.Disabled || user.SecurityVersion != sessionVersion ||
                user.CompanyId != current.CompanyId || user.IsMaster != current.IsMaster ||
                (tokenHash != null && (token == null || token.SecurityVersion != sessionVersion))
                ? "Su acceso fue retirado o su contraseña cambió. Inicie sesión nuevamente."
                : now < sessionStartedUtc || now - sessionStartedUtc >= SessionLifetime ||
                  (token != null && now >= token.ExpiresAtUtc)
                ? "La sesión venció después de 8 horas. Inicie sesión nuevamente."
                : now < lastActivityUtc || now - lastActivityUtc >= IdleTimeout
                ? "Sesión bloqueada por 15 minutos sin actividad. Inicie sesión nuevamente." : null;
            if (reason != null)
            {
                if (tokenHash != null) connection.Execute("DELETE FROM SessionToken WHERE TokenHash = ?", tokenHash);
                Logout();
                SessionInvalidated?.Invoke(this, reason);
                throw new InvalidOperationException(reason);
            }
            if (activity)
            {
                bool persist;
                lock (activityGate)
                {
                    this.lastActivityUtc = now;
                    persist = now - lastActivityPersistedUtc >= ActivityPersistInterval;
                    if (persist) lastActivityPersistedUtc = now;
                }
                if (token != null && persist) connection.Execute("UPDATE SessionToken SET LastUsedAt = ? WHERE Id = ?", now, token.Id);
            }
            else if (token != null)
            {
                // Periodic check: store the in-memory activity (from NoteActivity) once per interval.
                bool persist;
                lock (activityGate)
                {
                    persist = lastActivityUtc - lastActivityPersistedUtc >= ActivityPersistInterval;
                    if (persist) lastActivityPersistedUtc = lastActivityUtc;
                }
                if (persist) connection.Execute("UPDATE SessionToken SET LastUsedAt = ? WHERE Id = ?", lastActivityUtc, token.Id);
            }
            return current;
        }
    }


    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // Creates a token for the signed-in user. The caller keeps the token on the device; the database
    // only keeps its hash, so reading the database file is not enough to sign in.
    public async Task<string> IssueToken()
    {
        RequireSession();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await db.RunInTransactionAsync(c =>
        {
            var user = RequireSession();
            c.Insert(new SessionToken { UserId = user.UserId, TokenHash = Hash(token),
                CreatedAt = sessionStartedUtc, LastUsedAt = lastActivityUtc,
                SecurityVersion = sessionVersion, ExpiresAtUtc = sessionStartedUtc + SessionLifetime });
            tokenHash = Hash(token);
        });
        return token;
    }

    public async Task<bool> Resume(string? token)
    {
        Logout();
        await Initialize();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) return false;
        var hash = Hash(token);
        var resumed = false;
        await db.RunInTransactionAsync(c =>
        {
            var stored = c.Table<SessionToken>().FirstOrDefault(t => t.TokenHash == hash);
            if (stored == null) return;
            var user = c.Find<LocalUser>(stored.UserId);
            var now = clock.GetUtcNow().UtcDateTime;
            if (user == null || user.Disabled || stored.SecurityVersion != user.SecurityVersion ||
                now >= stored.ExpiresAtUtc || now < stored.CreatedAt || now < stored.LastUsedAt ||
                now - stored.LastUsedAt >= IdleTimeout)
            { c.Delete(stored); return; }
            StartSession(user, stored.CreatedAt, now);
            tokenHash = hash;
            c.Execute("UPDATE SessionToken SET LastUsedAt = ? WHERE Id = ?", now, stored.Id);
            resumed = true;
        });
        return resumed;
    }

    // Password recovery for someone with access to this computer (the database file lives in the
    // Windows profile). Only the local tool Tools/RestablecerContrasena calls it; the app never does.
    // It also revokes the user's remembered sessions.
    public Task ResetPassword(string username, string newPassword) => ResetPasswordCore(username, newPassword, false);

    private async Task ResetPasswordCore(string username, string newPassword, bool requireMaster)
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
            if (requireMaster) RequireMaster();
            c.Execute("UPDATE LocalUser SET Salt = ?, PasswordHash = ?, SecurityVersion = COALESCE(SecurityVersion, 0) + 1 WHERE Id = ?", user.Salt, user.PasswordHash, user.Id);
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
