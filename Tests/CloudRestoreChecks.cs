using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using GestionLibros.Data;
using GestionLibros.Models;

// A new installation downloads its data from the cloud instead of asking for a new master.
internal static class CloudRestoreChecks
{
    const string Secret = "Test-secret-123";
    public static async Task Run()
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS restore " + message); }
        static string NewPath() => Path.Combine(Path.GetTempPath(), $"fredi-restore-tests-{Guid.NewGuid():N}.db3");

        // Original computer: master, two companies with operators, accounts, a journal and a draft, all uploaded.
        var origin = new AppDatabase(NewPath());
        await origin.Setup("master", Secret); await origin.Login("master", Secret);
        await origin.AddCompany("A"); await origin.AddCompany("B");
        var companies = await origin.Companies(); var a = companies[0].Id; var b = companies[1].Id;
        await origin.AddUser("alpha", Secret, a); await origin.AddUser("bravo", Secret, b);
        await origin.Login("alpha", Secret);
        await origin.AddAccount(a, "1", "Activo", ""); await origin.AddAccount(a, "2", "Capital", "");
        var accounts = await origin.Accounts(a);
        await origin.SaveJournal(a, new Journal { Date = new DateTime(2026, 9, 1), Reference = "1", Type = "01", Concept = "Apertura" },
            [new() { AccountId = accounts[0].Id, DebitCents = 12345 }, new() { AccountId = accounts[1].Id, CreditCents = 12345 }]);
        await origin.Login("bravo", Secret);
        await origin.AddAccount(b, "9", "Solo B", "");
        var cloud = new FakeCloud(await origin.CloudDataset());
        foreach (var user in new[] { "master", "alpha", "bravo" })
        {
            await origin.Login(user, Secret);
            foreach (var item in await origin.PrepareCloudUploads()) await origin.AcknowledgeCloudUpload(item.Scope, item.RequestId, cloud.Accept(item));
        }

        // Fresh computer, the operator signs in: only its company arrives, and it is ready to keep accumulating.
        var fresh = new AppDatabase(NewPath());
        Check(await fresh.NeedsSetup() && await fresh.CanRestoreFromCloud(), "empty installation can be restored");
        using (var api = new CloudApi(new("https://example.invalid/"), cloud)) await fresh.RestoreFromCloud(api, " ALPHA ", Secret);
        Check(fresh.Session?.Username == "alpha" && !fresh.Session.IsMaster, "operator is signed in with the restored verifier");
        Check((await fresh.Companies()).Single().Id == a && (await fresh.Accounts(a)).Count == 2, "operator receives its company and catalog");
        var journal = (await fresh.Journals(a, 2026, 9)).Single();
        Check((await fresh.Lines(a, journal.Id)).Sum(l => l.DebitCents) == 12345, "journal lines keep their identifiers and amounts");
        Check(await fresh.CloudDataset() == cloud.Dataset, "installation joins the cloud dataset");
        Check((await fresh.CloudSyncStatus()).PendingScopes == 0, "restored data is not uploaded again");
        Check(!cloud.LastRestore!.Contains("\"bravo\"") && !cloud.LastRestore.Contains("Solo B"), "operator never receives another company or user verifier");
        await fresh.AddAccount(a, "3", "Nueva", "");
        var next = (await fresh.PrepareCloudUploads()).Single();
        Check(JsonSerializer.Deserialize<CloudUpload>(next.Payload)!.BaseRevision == cloud.Revision("company:" + a), "next upload continues the cloud revision");
        try { using var api = new CloudApi(new("https://example.invalid/"), cloud); await fresh.RestoreFromCloud(api, "alpha", Secret); throw new Exception("restore replaced local data"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("ya tiene")) { Check(true, "installation with accounting is never replaced"); }

        // A master created by mistake on an empty installation is replaced by the cloud copy.
        var stub = new AppDatabase(NewPath());
        await stub.Setup("prueba", "Stub-secret-123");
        using (var api = new CloudApi(new("https://example.invalid/"), cloud)) await stub.RestoreFromCloud(api, "master", Secret);
        Check(stub.Session?.IsMaster == true && (await stub.Companies()).Count == 2, "master restores every company over an empty stub");
        await stub.Login("bravo", Secret);
        Check((await stub.Accounts(b)).Single().Code == "9", "operators of a master restore can sign in locally");
        try { await stub.Login("prueba", "Stub-secret-123"); throw new Exception("stub user survived"); }
        catch (InvalidOperationException) { Check(true, "stub master is removed by the restore"); }

        // Wrong password: the cloud rejects it and nothing local changes.
        var rejected = new AppDatabase(NewPath());
        try { using var api = new CloudApi(new("https://example.invalid/"), cloud); await rejected.RestoreFromCloud(api, "alpha", "Wrong-secret-123"); throw new Exception("wrong password accepted"); }
        catch (CloudAuthenticationException) { Check(await rejected.NeedsSetup(), "rejected credentials leave the installation empty"); }
        Console.WriteLine("Cloud restore checks complete.");
    }

    // Mirrors gateway.sql: snapshots per scope, login by unique username, restore filtered by role.
    private sealed class FakeCloud(string dataset) : HttpMessageHandler
    {
        public string Dataset { get; } = dataset;
        public string? LastRestore { get; private set; }
        private readonly Dictionary<string, (long Revision, List<CloudRecord> Records)> scopes = [];
        private LocalUser? signedIn;
        public long Revision(string scope) => scopes[scope].Revision;
        public long Accept(CloudPending pending)
        {
            var upload = JsonSerializer.Deserialize<CloudUpload>(pending.Payload)!;
            var revision = (scopes.TryGetValue(upload.Scope, out var current) ? current.Revision : 0) + 1;
            scopes[upload.Scope] = (revision, upload.Records);
            return revision;
        }
        private IEnumerable<LocalUser> Users => scopes["admin"].Records.Where(r => r.Table == nameof(LocalUser)).Select(r => r.Data.Deserialize<LocalUser>()!);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken token)
        {
            var route = req.RequestUri!.AbsolutePath.TrimStart('/');
            if (route == CloudApi.LoginRoute)
            {
                var body = JsonSerializer.Deserialize<JsonElement>(await req.Content!.ReadAsStringAsync(token));
                var user = Users.SingleOrDefault(u => u.Username == body.GetProperty("username").GetString()!.Trim().ToLowerInvariant());
                var hash = user == null ? [] : Rfc2898DeriveBytes.Pbkdf2(body.GetProperty("password").GetString()!, Convert.FromBase64String(user.Salt), 210000, HashAlgorithmName.SHA256, 32);
                if (user == null || user.Disabled || Convert.ToBase64String(hash) != user.PasswordHash) return new(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":\"unauthorized\"}") };
                signedIn = user;
                return Json(new { token = "t", dataset = Dataset });
            }
            if (route == CloudApi.LogoutRoute) { signedIn = null; return Json(new { ok = true }); }
            if (route != CloudApi.RestoreRoute || signedIn == null) return new(HttpStatusCode.Unauthorized);
            var u = signedIn;
            bool Visible(string scope) => u.IsMaster || scope == "company:" + u.CompanyId || scope == "draft:" + u.Id;
            var records = scopes.SelectMany(s => s.Value.Records.Select(r => new CloudScopedRecord(s.Key, r.Table, r.Key, r.Data)))
                .Where(r => Visible(r.Scope) || (r.Scope == "admin" && ((r.Table == nameof(LocalUser) && r.Key == u.Id.ToString()) || (r.Table == nameof(Company) && r.Key == u.CompanyId.ToString())))).ToList();
            var response = Json(new CloudSnapshot(Dataset, u.Id, scopes.Where(s => Visible(s.Key)).Select(s => new CloudScopeRevision(s.Key, s.Value.Revision)).ToList(), records));
            LastRestore = await response.Content.ReadAsStringAsync(token);
            return response;
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
