using System.Text.Json;
using GestionLibros.Models;

namespace GestionLibros.Data;

public record CloudScopeRevision(string Scope, long Revision);
public record CloudScopedRecord(string Scope, string Table, string Key, JsonElement Data);
public record CloudSnapshot(string Dataset, int UserId, List<CloudScopeRevision> Scopes, List<CloudScopedRecord> Records);

public sealed partial class AppDatabase
{
    // The same entities the cloud triggers upload. Device session tokens never travel.
    private static readonly Dictionary<string, Type> CloudTables = new[]
    {
        typeof(Company), typeof(CatalogPackage), typeof(LocalUser), typeof(Account), typeof(PolicyType), typeof(Journal),
        typeof(JournalLine), typeof(ImportedBalance), typeof(MigrationPolicy), typeof(ImportBatch), typeof(DetailedPeriod), typeof(JournalDraft)
    }.ToDictionary(t => t.Name);
    private const string LocalDataCount = "SELECT (SELECT COUNT(*) FROM Company)+(SELECT COUNT(*) FROM CatalogPackage)+(SELECT COUNT(*) FROM Account)" +
        "+(SELECT COUNT(*) FROM Journal)+(SELECT COUNT(*) FROM ImportedBalance)+(SELECT COUNT(*) FROM JournalDraft)";

    // An installation without accounting data is filled entirely from the cloud. A master created on the
    // setup screen of an empty installation is not data and is replaced.
    public async Task<bool> CanRestoreFromCloud()
    {
        await Initialize();
        return await db.ExecuteScalarAsync<int>(LocalDataCount) == 0;
    }
    public async Task<bool> KnowsUser(string username)
    {
        await Initialize();
        username = username.Trim().ToLowerInvariant();
        return await db.Table<LocalUser>().Where(u => u.Username == username).CountAsync() > 0;
    }

    // Signs in to the cloud with the user's own credentials, copies everything that user may see and then
    // signs in locally against the restored verifier. An empty installation is replaced; otherwise the user's
    // groups are added to this one (same dataset only) and only groups without unsent changes are refreshed.
    // Revisions are kept so the next Acumular continues the same history instead of conflicting with it.
    public async Task RestoreFromCloud(CloudApi api, string username, string password, CancellationToken cancellation = default)
    {
        username = username.Trim().ToLowerInvariant();
        var empty = await CanRestoreFromCloud();
        if (!empty && await KnowsUser(username)) throw new InvalidOperationException("Este usuario ya está en este equipo. Use su contraseña local.");
        CloudSnapshot snapshot;
        try
        {
            var dataset = await api.Login(empty ? null : (await db.FindAsync<CloudIdentity>(1)).Dataset, username, password, cancellation);
            snapshot = await api.Restore(cancellation);
            if (!string.Equals(snapshot.Dataset, dataset, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("La nube entregó datos de otro conjunto.");
        }
        finally { await api.Logout(); }
        await ApplyCloudSnapshot(snapshot, username);
        await Login(username, password);
    }

    private async Task ApplyCloudSnapshot(CloudSnapshot snapshot, string username)
    {
        if (!Guid.TryParse(snapshot.Dataset, out _)) throw new InvalidOperationException("La nube no identificó el conjunto de datos.");
        var rows = (snapshot.Records ?? []).Select(r => CloudTables.TryGetValue(r.Table, out var type)
            ? r.Data.Deserialize(type) ?? throw new InvalidOperationException("La nube envió un registro vacío.")
            : throw new InvalidOperationException($"La nube envió una tabla desconocida: {r.Table}.")).ToList();
        var user = rows.OfType<LocalUser>().SingleOrDefault(u => u.Id == snapshot.UserId);
        if (user == null || user.Username != username || user.Disabled) throw new InvalidOperationException("La copia de la nube no contiene su usuario activo.");
        // Complete groups replace their local copy. An operator also receives its own user and company,
        // which are added to the administrative group without replacing the other users.
        var complete = (snapshot.Scopes ?? []).ToDictionary(s => s.Scope, s => s.Revision);
        var touched = complete.Keys.Concat((snapshot.Records ?? []).Select(r => r.Scope)).Distinct().ToList();
        await db.RunInTransactionAsync(c =>
        {
            var empty = c.ExecuteScalar<int>(LocalDataCount) == 0;
            if (empty)
            {
                foreach (var table in CloudTables.Keys.Append(nameof(SessionToken))) c.Execute($"DELETE FROM {table}");
                c.Execute("DELETE FROM CloudPending"); c.Execute("DELETE FROM CloudState");
                c.Execute("UPDATE CloudIdentity SET Dataset=? WHERE Id=1", snapshot.Dataset);
            }
            else
            {
                if (!string.Equals(c.Find<CloudIdentity>(1).Dataset, snapshot.Dataset, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Este equipo pertenece a otro conjunto de datos de la nube.");
                foreach (var scope in touched)
                    if (c.ExecuteScalar<int>("SELECT (SELECT COUNT(*) FROM CloudChange WHERE Scope=?)+(SELECT COUNT(*) FROM CloudPending WHERE Scope=?)", scope, scope) != 0)
                        throw new InvalidOperationException("Este equipo tiene cambios sin acumular que este usuario también puede ver. Entre con el usuario que los hizo y pulse Acumular antes de descargar otro usuario.");
                foreach (var scope in complete.Keys) ClearScope(c, scope);
                // Identifiers are generated per computer. A row created here may share its identifier with
                // another company's row in the cloud; merging would overwrite it, so nothing is merged.
                foreach (var row in rows)
                {
                    var map = c.GetMapping(row.GetType());
                    var existing = c.Query(map, $"SELECT * FROM \"{map.TableName}\" WHERE \"{map.PK.Name}\"=?", map.PK.GetValue(row)).SingleOrDefault();
                    if (existing != null && row is not (Company or CatalogPackage) && (row is not LocalUser incoming || ((LocalUser)existing).Username != incoming.Username))
                        throw new InvalidOperationException("Los registros de este equipo coinciden con los de otra contabilidad de la nube y no se pueden combinar. Descargue a este usuario en un equipo sin datos.");
                }
            }
            foreach (var row in rows) c.InsertOrReplace(row);
            // The restored rows are exactly the cloud revision: nothing new is pending to upload.
            if (empty) c.Execute("DELETE FROM CloudChange");
            else foreach (var scope in touched) c.Execute("DELETE FROM CloudChange WHERE Scope=?", scope);
            foreach (var (scope, revision) in complete) c.InsertOrReplace(new CloudState { Scope = scope, Revision = revision, SyncedUtc = DateTime.UtcNow });
        });
        Logout();
    }

    private static void ClearScope(SQLite.SQLiteConnection c, string scope)
    {
        if (scope == "admin") { c.Execute("DELETE FROM Company"); c.Execute("DELETE FROM CatalogPackage"); c.Execute("DELETE FROM LocalUser"); }
        else if (scope.StartsWith("draft:")) c.Execute("DELETE FROM JournalDraft WHERE UserId=?", int.Parse(scope[6..]));
        else if (scope.StartsWith("company:"))
        {
            var id = int.Parse(scope[8..]);
            c.Execute("DELETE FROM JournalLine WHERE JournalId IN (SELECT Id FROM Journal WHERE CompanyId=?)", id);
            foreach (var table in new[] { "Account", "PolicyType", "Journal", "ImportedBalance", "MigrationPolicy", "ImportBatch", "DetailedPeriod" })
                c.Execute($"DELETE FROM {table} WHERE CompanyId=?", id);
        }
        else throw new InvalidOperationException($"La nube envió un grupo desconocido: {scope}.");
    }
}
