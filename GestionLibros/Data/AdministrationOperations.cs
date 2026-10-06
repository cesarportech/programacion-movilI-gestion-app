using GestionLibros.Models;

namespace GestionLibros.Data;

// Tipos de Pólizas, Duplicar e Impresión de Pólizas, Usuarios Conectados and Sistema de Respaldos.
public sealed partial class AppDatabase
{
    public Task<List<PolicyType>> PolicyTypes(int companyId)
    {
        RequireCompany(companyId);
        return db.Table<PolicyType>().Where(t => t.CompanyId == companyId).OrderBy(t => t.Code).ToListAsync();
    }

    // Adds the type or, if the code exists, changes its description.
    public async Task SavePolicyType(int companyId, string code, string description)
    {
        RequireCompany(companyId);
        code = code.Trim(); description = description.Trim();
        if (code.Length == 0 || code.Length > 20) throw new InvalidOperationException("Indique un tipo de hasta 20 caracteres.");
        if (description.Length == 0 || description.Length > 80) throw new InvalidOperationException("Indique una descripción de hasta 80 caracteres.");
        await db.RunInTransactionAsync(c =>
        {
            if (c.Find<Company>(companyId) == null) throw new InvalidOperationException("Contabilidad no disponible.");
            var existing = c.Table<PolicyType>().FirstOrDefault(t => t.CompanyId == companyId && t.Code == code);
            if (existing == null) c.Insert(new PolicyType { CompanyId = companyId, Code = code, Description = description });
            else { existing.Description = description; c.Update(existing); }
        });
    }

    public async Task DeletePolicyType(int companyId, string code)
    {
        RequireCompany(companyId);
        await db.RunInTransactionAsync(c =>
        {
            var type = c.Table<PolicyType>().FirstOrDefault(t => t.CompanyId == companyId && t.Code == code)
                ?? throw new InvalidOperationException("Tipo de póliza no disponible.");
            if (c.Table<Journal>().Any(j => j.CompanyId == companyId && j.Type == code))
                throw new InvalidOperationException("No puede borrar un tipo que ya tiene pólizas.");
            c.Delete(type);
        });
    }

    // Copies a póliza and its movements under a new reference and date (same validation as a new one).
    public async Task DuplicateJournal(int companyId, int journalId, string reference, DateTime date)
    {
        RequireCompany(companyId);
        var source = await db.FindAsync<Journal>(journalId);
        if (source == null || source.CompanyId != companyId) throw new InvalidOperationException("Póliza no disponible.");
        var lines = await Lines(companyId, journalId);
        await SaveJournal(companyId, new Journal { Date = date, Type = source.Type, Reference = reference, Concept = source.Concept },
            lines.Select(l => new JournalLine { AccountId = l.AccountId, Concept = l.Concept, DebitCents = l.DebitCents, CreditCents = l.CreditCents }).ToList());
    }

    // Pólizas between two months of a year (optionally one type), with their movements, for printing.
    public async Task<List<PolicyPrint>> PoliciesForPrint(int companyId, int year, int fromMonth, int throughMonth, string type = "")
    {
        RequireCompany(companyId);
        if (fromMonth is < 1 or > 12 || throughMonth is < 1 or > 12 || fromMonth > throughMonth)
            throw new InvalidOperationException("Indique un periodo válido.");
        type = type.Trim();
        var journals = (await JournalsOfYear(companyId, year)).Where(j => j.Month >= fromMonth && j.Month <= throughMonth && (type.Length == 0 || j.Type == type))
            .OrderBy(j => j.Type, StringComparer.Ordinal).ThenBy(j => j.Reference, StringComparer.Ordinal).ToList();
        var accounts = (await Accounts(companyId)).ToDictionary(a => a.Id);
        var types = (await PolicyTypes(companyId)).ToDictionary(t => t.Code, t => t.Description);
        var result = new List<PolicyPrint>();
        foreach (var journal in journals)
        {
            var lines = (await Lines(companyId, journal.Id)).Select(l => new PolicyPrintLine(
                accounts.TryGetValue(l.AccountId, out var a) ? a.Code : "?", a?.Description ?? "?",
                l.Concept.Length > 0 ? l.Concept : journal.Concept, l.DebitCents, l.CreditCents)).ToList();
            result.Add(new(journal, types.GetValueOrDefault(journal.Type, ""), lines));
        }
        return result;
    }

    // Usuarios Conectados: users with a remembered session (signed in and not signed out).
    public async Task<List<SessionInfo>> ActiveSessions()
    {
        RequireMaster();
        var users = (await db.Table<LocalUser>().ToListAsync()).ToDictionary(u => u.Id);
        return (await db.Table<SessionToken>().ToListAsync())
            .Where(t => users.ContainsKey(t.UserId))
            .Select(t => new SessionInfo(users[t.UserId].Username, users[t.UserId].IsMaster, t.CreatedAt, t.LastUsedAt))
            .OrderByDescending(s => s.LastUsedAt).ToList();
    }

    // Sistema de Respaldos: a consistent copy of the whole local database (SQLite online backup).
    public async Task<string> Backup(string folder)
    {
        RequireMaster();
        await Initialize();
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"fredi-respaldo-{DateTime.Now:yyyyMMdd-HHmmss}.db3");
        await db.BackupAsync(file);
        return file;
    }
}
