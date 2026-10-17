using GestionLibros.Data;
using GestionLibros.Models;
using SQLite;

// Input activity is tracked in memory (no disk write per key/click) without weakening the idle lock.
static class ActivityChecks
{
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 17, 9, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    static int passed;
    static void Check(bool condition, string description) { if (!condition) throw new Exception(description); passed++; Console.WriteLine("PASS ACTIVITY " + description); }

    public static async Task Run()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fredi-activity-{Guid.NewGuid():N}.db3");
        var clock = new TestClock();
        var db = new AppDatabase(path, clock);
        await db.Setup("master", "Activity-test-1234");
        var company = await TestSupport.NewCompany(db, "Activity-test-1234", "Empresa Actividad");
        await db.IssueToken();
        var raw = new SQLiteAsyncConnection(path);
        async Task<DateTime> LastUsed() => (await raw.Table<SessionToken>().ToListAsync()).Single().LastUsedAt;
        var issued = await LastUsed();

        clock.Now = clock.Now.AddSeconds(10);
        for (var i = 0; i < 200; i++) db.NoteActivity();
        Check(await LastUsed() == issued, "200 key presses write nothing to the database");
        db.ValidateSession(false);
        Check(await LastUsed() == issued, "Periodic check within a minute does not write either");

        clock.Now = clock.Now.AddSeconds(65);
        db.NoteActivity();
        var typedAt = clock.Now.UtcDateTime;
        db.ValidateSession(false);
        Check(await LastUsed() == typedAt, "Periodic check stores the latest in-memory activity once per minute");

        for (var minute = 0; minute < 40; minute += 10)
        {
            clock.Now = clock.Now.AddMinutes(10);
            db.NoteActivity();
            db.ValidateSession(false);
        }
        Check(db.Session?.CompanyId == company, "Regular activity keeps the session open past the 15-minute idle limit");

        clock.Now = clock.Now.AddMinutes(16);
        db.NoteActivity(); // a key pressed after the idle limit must not revive the session
        try { db.ValidateSession(false); throw new Exception("Idle session revived by activity"); }
        catch (InvalidOperationException) { Check(db.Session == null, "Activity after 15 idle minutes does not revive the session"); }
        Console.WriteLine($"All {passed} activity checks passed.");
    }
}
