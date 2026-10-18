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

    // Only a device without accounting data is filled from the cloud, so nothing local can be lost.
    // A master created on the setup screen of an empty installation is not data.
    public async Task<bool> CanRestoreFromCloud()
    {
        await Initialize();
        return await db.ExecuteScalarAsync<int>(LocalDataCount) == 0;
    }

    // New installation: signs in to the cloud with the user's own credentials, copies everything that user
    // may see and then signs in locally against the restored verifier. Revisions are kept so the next
    // Acumular continues the same history instead of conflicting with it.
    public async Task RestoreFromCloud(CloudApi api, string username, string password, CancellationToken cancellation = default)
    {
        if (!await CanRestoreFromCloud()) throw new InvalidOperationException("Este equipo ya tiene contabilidades; no se reemplazan con la copia de la nube.");
        username = username.Trim().ToLowerInvariant();
        CloudSnapshot snapshot;
        try
        {
            var dataset = await api.Login(null, username, password, cancellation);
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
        await db.RunInTransactionAsync(c =>
        {
            if (c.ExecuteScalar<int>(LocalDataCount) != 0) throw new InvalidOperationException("Este equipo ya tiene contabilidades; no se reemplazan con la copia de la nube.");
            foreach (var table in CloudTables.Keys.Append(nameof(SessionToken))) c.Execute($"DELETE FROM {table}");
            foreach (var row in rows) c.InsertOrReplace(row);
            // The restored rows are exactly the cloud revision: nothing is pending to upload.
            c.Execute("DELETE FROM CloudChange"); c.Execute("DELETE FROM CloudPending"); c.Execute("DELETE FROM CloudState");
            foreach (var scope in snapshot.Scopes ?? []) c.Insert(new CloudState { Scope = scope.Scope, Revision = scope.Revision, SyncedUtc = DateTime.UtcNow });
            c.Execute("UPDATE CloudIdentity SET Dataset=? WHERE Id=1", snapshot.Dataset);
        });
        Logout();
    }
}
