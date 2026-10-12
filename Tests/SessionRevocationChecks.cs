using GestionLibros.Data;
using GestionLibros.Models;
using SQLite;

static class SessionRevocationChecks
{
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public static async Task Run()
    {
        int passed = 0;
        void Check(bool ok, string text) { if (!ok) throw new Exception(text); passed++; Console.WriteLine("PASS REVOCATION " + text); }
        async Task Denied(Func<Task> work, string text)
        { try { await work(); } catch (InvalidOperationException) { Check(true, text); return; } throw new Exception("Expected denial: " + text); }
        var clock = new TestClock();
        var path = Path.Combine(Path.GetTempPath(), $"fredi-security-{Guid.NewGuid():N}.db3");
        var admin = new AppDatabase(path, clock);
        await admin.Setup("master", "Master-secret-123");
        var company = await TestSupport.NewCompany(admin, "Master-secret-123", "Security fixture");
        await admin.AddAccount(company, "1", "Caja", ""); await admin.AddAccount(company, "2", "Capital", "");
        var accounts = await admin.Accounts(company);
        var userId = admin.Session!.UserId;
        await admin.Login("master", "Master-secret-123");
        AppDatabase Client() => new(path, clock);
        var open = Client(); var memoryOnly = Client();
        await TestSupport.LoginAs(open, company); await TestSupport.LoginAs(memoryOnly, company);
        var token = await open.IssueToken();
        int invalidations = 0; open.SessionInvalidated += (_, _) => invalidations++;
        await admin.SetUserPassword(userId, "Replacement-123");
        await Denied(() => open.Accounts(company), "Open remembered session cannot read after password change");
        Check(open.Session == null && invalidations == 1, "Revocation clears memory and notifies UI exactly once");
        await Denied(() => memoryOnly.SaveJournal(company, new Journal { Type = "01", Reference = "forbidden", Date = new(2026, 10, 7), Concept = "No write" },
            [new() { AccountId = accounts[0].Id, DebitCents = 100 }, new() { AccountId = accounts[1].Id, CreditCents = 100 }]), "Session without token cannot save after password change");
        Check(!await Client().Resume(token), "Old token cannot resume");
        await open.Login(TestSupport.UserFor(company), "Replacement-123");
        Check((await open.Journals(company, 2026, 10)).Count == 0, "Rejected save leaves no journal");
        token = await open.IssueToken();
        await admin.SetUserActive(userId, false);
        Check((await admin.UserList()).Single(u => u.Id == userId).Disabled, "Administrator sees suspended state");
        await Denied(() => open.Balances(company, 2026, 10), "Suspended open session cannot consult");
        await Denied(() => Client().Login(TestSupport.UserFor(company), "Replacement-123"), "Suspended user cannot log in");
        Check(!await Client().Resume(token), "Suspended token rejected");
        await admin.SetUserActive(userId, true);
        Check(!await Client().Resume(token), "Reactivation never restores old token");
        await open.Login(TestSupport.UserFor(company), "Replacement-123");
        await Denied(() => open.SetUserActive(userId, false), "Operator cannot suspend users");
        await Denied(() => admin.SetUserActive(admin.Session!.UserId, false), "Master cannot be suspended");
        await open.IssueToken();
        clock.Now += TimeSpan.FromMinutes(14);
        open.ValidateSession();
        clock.Now += TimeSpan.FromMinutes(1);
        await Denied(() => open.Accounts(company), "Background validation does not postpone 15-minute lock");
        Check(open.Session == null, "Idle timeout clears session");
        await open.Login(TestSupport.UserFor(company), "Replacement-123"); token = await open.IssueToken();
        clock.Now += TimeSpan.FromMinutes(14); open.ValidateSession(true);
        clock.Now += TimeSpan.FromMinutes(14); Check(await Client().Resume(token), "Real activity renews idle time and survives reopening");
        // Continuous activity must not extend the absolute eight-hour lifetime.
        await open.Login(TestSupport.UserFor(company), "Replacement-123"); token = await open.IssueToken();
        for (int i = 0; i < 47; i++) { clock.Now += TimeSpan.FromMinutes(10); open.ValidateSession(true); }
        clock.Now += TimeSpan.FromMinutes(10);
        await Denied(() => open.Accounts(company), "Eight-hour lifetime expires despite continuous activity");
        Check(!await Client().Resume(token), "Expired token cannot resume");
        await open.Login(TestSupport.UserFor(company), "Replacement-123"); token = await open.IssueToken();
        clock.Now += TimeSpan.FromMinutes(15);
        Check(!await Client().Resume(token), "Closed idle session cannot bypass lock by reopening");
        await open.Login(TestSupport.UserFor(company), "Replacement-123"); token = await open.IssueToken();
        var copy = Client(); Check(await copy.Resume(token), "Second live instance resumes valid token");
        await copy.SignOut();
        await Denied(() => open.Accounts(company), "Revoked device token invalidates already-open instance");
        await open.Login(TestSupport.UserFor(company), "Replacement-123");
        await admin.Login("master", "Master-secret-123");
        await admin.DeleteUser(userId);
        await Denied(() => open.Accounts(company), "Deleted user's open memory session cannot read");
        Check((await admin.Companies()).Any(c => c.Id == company), "Deleting user preserves company");
        // Simulate an existing schema: remove only new columns in this synthetic database.
        var sql = new SQLiteAsyncConnection(path);
        await sql.ExecuteAsync("ALTER TABLE LocalUser DROP COLUMN Disabled");
        await sql.ExecuteAsync("ALTER TABLE LocalUser DROP COLUMN SecurityVersion");
        await sql.ExecuteAsync("ALTER TABLE SessionToken DROP COLUMN SecurityVersion");
        await sql.ExecuteAsync("ALTER TABLE SessionToken DROP COLUMN ExpiresAtUtc");
        var migrated = Client(); await migrated.Login("master", "Master-secret-123");
        Check((await migrated.Companies()).Any(c => c.Id == company), "Existing database migrates without losing users or companies");
        await migrated.SetUserPassword(migrated.Session!.UserId, "Master-new-123");
        await Denied(() => migrated.Companies(), "Master password change also revokes its own open session");
        Console.WriteLine($"All {passed} session revocation checks passed.");
    }
}
