using System.Net;
using System.Text.Json;
using GestionLibros.Data;
using GestionLibros.Models;

internal static class CloudSyncChecks
{
    public static async Task Run()
    {
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS cloud " + message); }
        var path = Path.Combine(Path.GetTempPath(), $"fredi-cloud-tests-{Guid.NewGuid():N}.db3");
        var db = new AppDatabase(path);
        await db.Setup("master", "Test-secret-123"); await db.Login("master", "Test-secret-123");
        await db.AddCompany("A"); await db.AddCompany("B");
        var companies = await db.Companies();
        await db.AddUser("alpha", "Test-secret-123", companies[0].Id);
        await db.AddUser("bravo", "Test-secret-123", companies[1].Id);
        var admin = (await db.PrepareCloudUploads()).Single(x => x.Scope == "admin");
        Check(!admin.Payload.Contains("SessionToken"), "device session tokens are excluded");
        await db.AddCompany("Added while offline");
        Check((await db.PrepareCloudUploads()).Single(x => x.Scope == "admin").Payload == admin.Payload, "unacknowledged request is immutable after later edits");
        await db.AcknowledgeCloudUpload("admin", admin.RequestId, 1);
        var second = (await db.PrepareCloudUploads()).Single(x => x.Scope == "admin");
        Check(second.RequestId != admin.RequestId && second.Payload.Contains("Added while offline"), "ACK preserves changes made during upload");
        await db.AcknowledgeCloudUpload("admin", second.RequestId, 2);
        Check((await db.CloudSyncStatus()).PendingScopes == 0, "confirmed changes leave the queue");
        await db.Login("alpha", "Test-secret-123");
        await db.AddAccount(companies[0].Id, "1.12101", "Cash", "");
        var upload = (await db.PrepareCloudUploads()).Single();
        Check(upload.Scope == "company:" + companies[0].Id && !upload.Payload.Contains("PasswordHash"), "operator uploads only own company without user verifiers");
        var reopened = new AppDatabase(path); await reopened.Login("alpha", "Test-secret-123");
        Check((await reopened.PrepareCloudUploads()).Single().Payload == upload.Payload, "outbox survives restart with identical request id and payload");
        using (var offline = new CloudApi(new("https://example.invalid/"), new Offline()))
        {
            try { await offline.Login(await reopened.CloudDataset(), "alpha", "not-a-real-password"); throw new Exception("offline connection accepted"); }
            catch (HttpRequestException) { Check((await reopened.PrepareCloudUploads()).Single().RequestId == upload.RequestId, "connection failure leaves the durable request intact"); }
        }
        await db.Login("bravo", "Test-secret-123");
        Check((await db.PrepareCloudUploads()).Count == 0, "other company cannot read pending payload");
        try { await db.AcknowledgeCloudUpload(upload.Scope, upload.RequestId, 1); throw new Exception("foreign ACK accepted"); }
        catch (InvalidOperationException) { Check(true, "other company cannot acknowledge a pending upload"); }
        await reopened.AcknowledgeCloudUpload(upload.Scope, upload.RequestId, 1);
        // Simulate a committed delete and a rolled-back change at the SQLite transaction boundary.
        using var raw = new SQLite.SQLiteConnection(path);
        raw.Execute("DELETE FROM Account WHERE CompanyId=?", companies[0].Id);
        var deletion = (await reopened.PrepareCloudUploads()).Single();
        Check(JsonSerializer.Deserialize<CloudUpload>(deletion.Payload)!.Records.Count == 0, "deletion is represented by a new empty snapshot");
        await reopened.AcknowledgeCloudUpload(deletion.Scope, deletion.RequestId, 2);
        try { raw.RunInTransaction(() => { raw.Insert(new Account { CompanyId = companies[0].Id, Code = "1" }); throw new InvalidOperationException(); }); } catch (InvalidOperationException) { }
        Check((await reopened.PrepareCloudUploads()).Count == 0, "rolled-back transaction does not create a sync change");
        using var api = new CloudApi(new("https://example.invalid/"), new Stub());
        await api.Login(await reopened.CloudDataset(), "alpha", "not-a-real-password");
        try { await api.Upload(deletion); throw new Exception("conflict ignored"); } catch (InvalidOperationException ex) { Check(ex.Message.Contains("otra instalación"), "HTTP conflict never acknowledges or overwrites"); }
        Check((await reopened.CloudSyncStatus()).PendingScopes == 0, "network client cannot mutate local queue");
        Console.WriteLine("Cloud sync checks complete.");
    }
    private sealed class Stub : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken token) => Task.FromResult(
            new HttpResponseMessage(req.RequestUri!.AbsolutePath.EndsWith(CloudApi.LoginRoute) ? HttpStatusCode.OK : HttpStatusCode.Conflict)
            { Content = new StringContent("{\"token\":\"test\"}") });
    }
    private sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken token) => throw new HttpRequestException("Synthetic offline connection");
    }
}
