using System.Text.Json;
using GestionLibros.Models;
using SQLite;

namespace GestionLibros.Data;

public class CloudState
{
    [PrimaryKey] public string Scope { get; set; } = "";
    public long Revision { get; set; }
    public DateTime SyncedUtc { get; set; }
}
public class CloudIdentity
{
    [PrimaryKey] public int Id { get; set; } = 1;
    public string Dataset { get; set; } = "";
}
public class CloudChange
{
    [PrimaryKey, AutoIncrement] public long Id { get; set; }
    [Indexed] public string Scope { get; set; } = "";
}
public class CloudPending
{
    [PrimaryKey] public string Scope { get; set; } = "";
    public string RequestId { get; set; } = "";
    public long Watermark { get; set; }
    public string Payload { get; set; } = "";
}
public record CloudRecord(string Table, string Key, JsonElement Data);
public record CloudUpload(string Dataset, string Scope, string RequestId, long BaseRevision, List<CloudRecord> Records);
public record CloudStatus(int PendingScopes, DateTime? LastUpload);

public sealed partial class AppDatabase
{
    // Triggers participate in the same transaction as the accounting change, including deletions.
    // Pending payloads are immutable until ACK: a lost HTTP response can be retried safely.
    private async Task InitializeCloudTracking()
    {
        await db.CreateTableAsync<CloudIdentity>();
        await db.CreateTableAsync<CloudState>();
        await db.CreateTableAsync<CloudChange>();
        await db.CreateTableAsync<CloudPending>();
        await db.RunInTransactionAsync(c =>
        {
            if (c.Find<CloudIdentity>(1) == null)
            {
                c.Insert(new CloudIdentity { Dataset = Guid.NewGuid().ToString() });
                c.Insert(new CloudChange { Scope = "admin" });
                foreach (var company in c.Table<Company>()) c.Insert(new CloudChange { Scope = "company:" + company.Id });
            }
            foreach (var table in new[] { "Company", "CatalogPackage", "LocalUser", "Account", "PolicyType", "Journal", "JournalLine", "ImportedBalance", "MigrationPolicy", "ImportBatch", "DetailedPeriod", "JournalDraft" })
            {
                foreach (var operation in new[] { "INSERT", "UPDATE", "DELETE" })
                {
                    var row = operation == "DELETE" ? "OLD" : "NEW";
                    string Scope(string r) => table switch
                    {
                        "Company" or "CatalogPackage" or "LocalUser" => "'admin'",
                        "JournalLine" => $"'company:' || (SELECT CompanyId FROM Account WHERE Id = {r}.AccountId)",
                        "JournalDraft" => $"'draft:' || {r}.UserId",
                        _ => $"'company:' || {r}.CompanyId"
                    };
                    var statements = $"INSERT INTO CloudChange (Scope) SELECT {Scope(row)} WHERE {Scope(row)} IS NOT NULL;";
                    if (operation == "UPDATE") statements += $" INSERT INTO CloudChange (Scope) SELECT {Scope("OLD")} WHERE {Scope("OLD")} IS NOT NULL;";
                    c.Execute($"CREATE TRIGGER IF NOT EXISTS cloud_{table}_{operation} AFTER {operation} ON {table} BEGIN {statements} END");
                }
            }
            // Include existing recoverable drafts without mixing them into posted journals.
            foreach (var userId in c.Table<JournalDraft>().Select(d => d.UserId).Distinct())
                if (c.Find<CloudState>("draft:" + userId) == null && !c.Table<CloudChange>().Any(x => x.Scope == "draft:" + userId))
                    c.Insert(new CloudChange { Scope = "draft:" + userId });
        });
    }

    private bool CanSync(string scope)
    {
        var user = RequireSession();
        return user.IsMaster || scope == "company:" + user.CompanyId || scope == "draft:" + user.UserId;
    }
    public async Task<string> CloudDataset()
    {
        await Initialize(); RequireSession();
        return (await db.FindAsync<CloudIdentity>(1)).Dataset;
    }
    public async Task<CloudStatus> CloudSyncStatus()
    {
        await Initialize(); RequireSession();
        var changes = await db.Table<CloudChange>().ToListAsync();
        var pending = await db.Table<CloudPending>().ToListAsync();
        var states = await db.Table<CloudState>().ToListAsync();
        return new(changes.Select(x => x.Scope).Concat(pending.Select(x => x.Scope)).Distinct().Count(CanSync),
            states.Where(x => CanSync(x.Scope)).Select(x => (DateTime?)x.SyncedUtc).Max());
    }
    public async Task<List<CloudPending>> PrepareCloudUploads()
    {
        await Initialize(); RequireSession();
        List<CloudPending> result = [];
        await db.RunInTransactionAsync(c =>
        {
            RequireSession();
            var scopes = c.Table<CloudChange>().ToList().Select(x => x.Scope)
                .Concat(c.Table<CloudPending>().ToList().Select(x => x.Scope)).Distinct().Where(CanSync)
                .OrderBy(x => x == "admin" ? 0 : x.StartsWith("company:") ? 1 : 2).ThenBy(x => x);
            foreach (var scope in scopes)
            {
                var existing = c.Find<CloudPending>(scope);
                if (existing != null) { result.Add(existing); continue; }
                List<CloudRecord> records = [];
                void Add<T>(IEnumerable<T> items, Func<T, object> key) => records.AddRange(items.Select(x => new CloudRecord(typeof(T).Name, key(x).ToString()!, JsonSerializer.SerializeToElement(x))));
                if (scope == "admin")
                {
                    Add(c.Table<Company>(), x => x.Id);
                    Add(c.Table<CatalogPackage>(), x => x.Id);
                    Add(c.Table<LocalUser>(), x => x.Id);
                }
                else if (scope.StartsWith("draft:"))
                {
                    var id = int.Parse(scope[6..]);
                    Add(c.Table<JournalDraft>().Where(x => x.UserId == id), x => x.Id);
                }
                else
                {
                    var id = int.Parse(scope[8..]);
                    Add(c.Table<Account>().Where(x => x.CompanyId == id), x => x.Id);
                    Add(c.Table<PolicyType>().Where(x => x.CompanyId == id), x => x.Id);
                    var journals = c.Table<Journal>().Where(x => x.CompanyId == id).ToList();
                    Add(journals, x => x.Id);
                    Add(c.Query<JournalLine>("SELECT l.* FROM JournalLine l JOIN Journal j ON j.Id=l.JournalId WHERE j.CompanyId=?", id), x => x.Id);
                    Add(c.Table<ImportedBalance>().Where(x => x.CompanyId == id), x => x.Id);
                    Add(c.Table<MigrationPolicy>().Where(x => x.CompanyId == id), x => x.CompanyId);
                    Add(c.Table<ImportBatch>().Where(x => x.CompanyId == id), x => x.Id);
                    Add(c.Table<DetailedPeriod>().Where(x => x.CompanyId == id), x => x.Key);
                }
                var request = new CloudUpload(c.Find<CloudIdentity>(1).Dataset, scope, Guid.NewGuid().ToString(), c.Find<CloudState>(scope)?.Revision ?? 0, records);
                var item = new CloudPending { Scope = scope, RequestId = request.RequestId,
                    Watermark = c.ExecuteScalar<long>("SELECT COALESCE(MAX(Id),0) FROM CloudChange WHERE Scope=?", scope), Payload = JsonSerializer.Serialize(request) };
                c.Insert(item); result.Add(item);
            }
        });
        return result;
    }
    public async Task AcknowledgeCloudUpload(string scope, string requestId, long revision)
    {
        if (!CanSync(scope)) throw new InvalidOperationException("No tiene acceso a esta sincronización.");
        await db.RunInTransactionAsync(c =>
        {
            if (!CanSync(scope)) throw new InvalidOperationException("La sesión cambió.");
            var item = c.Find<CloudPending>(scope);
            if (item == null || item.RequestId != requestId) throw new InvalidOperationException("La confirmación no corresponde al envío pendiente.");
            var request = JsonSerializer.Deserialize<CloudUpload>(item.Payload)!;
            if (revision != request.BaseRevision + 1) throw new InvalidOperationException("La revisión recibida no es válida.");
            c.InsertOrReplace(new CloudState { Scope = scope, Revision = revision, SyncedUtc = DateTime.UtcNow });
            c.Execute("DELETE FROM CloudChange WHERE Scope=? AND Id<=?", scope, item.Watermark);
            c.Delete(item);
        });
    }
}
