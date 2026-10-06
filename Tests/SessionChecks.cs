using GestionLibros.Data;
using GestionLibros.Models;
using SQLite;

static class SessionChecks
{
    static int passed;
    static void Check(bool condition, string description) { if (!condition) throw new Exception(description); passed++; Console.WriteLine("PASS SESSION " + description); }

    // Each "new AppDatabase(path)" simulates closing the window and opening the program again.
    public static async Task Run(string path, int operatorCompany, int foreignCompany)
    {
        var app = new AppDatabase(path);
        await app.Login("master", "Test-only-secret-123");
        var masterToken = await app.IssueToken();
        var stored = await new SQLiteAsyncConnection(path).Table<SessionToken>().ToListAsync();
        Check(stored.Count == 1 && stored[0].TokenHash != masterToken && !stored[0].TokenHash.Contains(masterToken), "Database keeps only the token hash");

        var reopened = new AppDatabase(path);
        Check(await reopened.Resume(masterToken) && reopened.Session?.Username == "master" && reopened.Session.IsMaster, "Closing the window keeps the session");
        Check(!await reopened.Resume("token-inventado") && reopened.Session == null, "Unknown token is rejected");
        Check(!await reopened.Resume("") && !await reopened.Resume(null), "Empty token is rejected");
        Check(!await reopened.Resume(masterToken[..^1] + (masterToken[^1] == 'A' ? 'B' : 'A')), "Altered token is rejected");

        var operatorApp = new AppDatabase(path);
        await operatorApp.Login("operador", "Operator-secret-123");
        var operatorToken = await operatorApp.IssueToken();
        var operatorReopened = new AppDatabase(path);
        Check(await operatorReopened.Resume(operatorToken) && operatorReopened.Session?.CompanyId == operatorCompany, "Resumed operator keeps their company");
        try { await operatorReopened.Accounts(foreignCompany); throw new Exception("Resumed operator read another company"); }
        catch (InvalidOperationException) { Check(true, "Resumed operator is still limited to their company"); }

        operatorReopened.Logout();
        Check(await new AppDatabase(path).Resume(operatorToken), "In-memory logout alone does not revoke the token");
        await operatorReopened.Resume(operatorToken);
        await operatorReopened.SignOut();
        Check(operatorReopened.Session == null, "Cerrar sesión clears the session");
        Check(!await new AppDatabase(path).Resume(operatorToken), "Cerrar sesión revokes the token");
        Check(await new AppDatabase(path).Resume(masterToken), "Signing out on one session leaves other sessions valid");

        var fresh = new AppDatabase(path);
        await fresh.SignOut();
        Check(await new AppDatabase(path).Resume(masterToken), "Signing out without a token revokes nothing");
        // Local password recovery (Tools/RestablecerContrasena).
        var recovery = new AppDatabase(path);
        await recovery.Login("operador", "Operator-secret-123");
        var operatorRemembered = await recovery.IssueToken();
        await recovery.ResetPassword("OPERADOR", "Nueva-clave-456");
        Check(!await new AppDatabase(path).Resume(operatorRemembered), "Password reset closes the user's remembered sessions");
        try { await new AppDatabase(path).Login("operador", "Operator-secret-123"); throw new Exception("Old password still works"); }
        catch (InvalidOperationException) { Check(true, "Old password stops working after reset"); }
        var afterReset = new AppDatabase(path);
        await afterReset.Login("operador", "Nueva-clave-456");
        Check(afterReset.Session?.CompanyId == operatorCompany && afterReset.Session.IsMaster == false, "New password works and keeps role and company");
        try { await recovery.ResetPassword("operador", "corta"); throw new Exception("Short password accepted"); }
        catch (InvalidOperationException) { Check(true, "Reset rejects passwords under 10 characters"); }
        try { await recovery.ResetPassword("nadie", "Otra-clave-789"); throw new Exception("Unknown user accepted"); }
        catch (InvalidOperationException) { Check(true, "Reset rejects unknown users"); }
        Check(await new AppDatabase(path).Resume(masterToken), "Resetting one user leaves other users' sessions");
        await recovery.ResetPassword("operador", "Operator-secret-123");
        Console.WriteLine($"All {passed} session checks passed.");
    }
}
