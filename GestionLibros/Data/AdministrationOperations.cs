using GestionLibros.Models;

namespace GestionLibros.Data;

// Tipos de Pólizas, Duplicar e Impresión de Pólizas, Usuarios Conectados and Sistema de Respaldos.
public sealed partial class AppDatabase
{
    public async Task<List<CatalogPackage>> CatalogPackages()
    {
        RequireSession();
        return await db.Table<CatalogPackage>().OrderBy(p => p.Name).ToListAsync();
    }

    public async Task DeleteCatalogPackage(int id)
    {
        RequireMaster();
        await db.RunInTransactionAsync(c =>
        {
            RequireMaster();
            var package = c.Find<CatalogPackage>(id) ?? throw new InvalidOperationException("El catálogo ya no existe.");
            if (c.Table<Company>().Any(x => x.CatalogPackageId == id))
                throw new InvalidOperationException("Este catálogo está asignado a una contabilidad y no se puede eliminar. Puede precargar otro catálogo para contabilidades nuevas.");
            c.Delete(package);
        });
    }
    public async Task<int> PreloadCatalog(string name, IReadOnlyList<CatalogAccount> accounts)
    {
        RequireMaster(); name = name.Trim();
        if (name.Length == 0 || name.Length > 120) throw new InvalidOperationException("Indique un nombre de catálogo de hasta 120 caracteres.");
        var clean = accounts.Select(a => new CatalogAccount(a.Code.Trim(), a.ParentCode.Trim(), a.Description.Trim())).ToList();
        ValidateCatalogPackage(clean);
        var package = new CatalogPackage { Name = name, Payload = System.Text.Json.JsonSerializer.Serialize(clean), CreatedBy = RequireSession().Username, CreatedUtc = DateTime.UtcNow };
        await db.RunInTransactionAsync(c =>
        {
            RequireMaster();
            if (c.Table<CatalogPackage>().Any(p => p.Name == name)) throw new InvalidOperationException("Ese catálogo ya existe. Use otro nombre para una versión nueva.");
            c.Insert(package);
        });
        return package.Id;
    }

    public async Task<int> CreateCompanyFromCatalog(string name, int packageId)
    {
        RequireMaster(); name = name.Trim();
        if (name.Length == 0 || name.Length > 120) throw new InvalidOperationException("Indique un nombre de contabilidad de hasta 120 caracteres.");
        var company = new Company { Name = name, CatalogPackageId = packageId };
        await db.RunInTransactionAsync(c =>
        {
            RequireMaster();
            var package = c.Find<CatalogPackage>(packageId) ?? throw new InvalidOperationException("Seleccione un catálogo precargado.");
            if (c.Table<Company>().Any(x => x.Name == name)) throw new InvalidOperationException("Esta contabilidad ya existe.");
            var accounts = System.Text.Json.JsonSerializer.Deserialize<List<CatalogAccount>>(package.Payload) ?? throw new InvalidOperationException("Catálogo vacío.");
            ValidateCatalogPackage(accounts);
            var map = accounts.ToDictionary(a => a.Code);
            c.Insert(company);
            foreach (var account in accounts)
            {
                int level = 1; var current = account;
                while (current.ParentCode.Length > 0) { current = map[current.ParentCode]; level++; }
                c.Insert(new Account { CompanyId = company.Id, Code = account.Code, ParentCode = account.ParentCode,
                    Description = account.Description, Level = level, ChangedBy = RequireSession().Username, ChangedAt = DateTime.Now });
            }
        });
        return company.Id;
    }
    private static void ValidateCatalogPackage(IReadOnlyList<CatalogAccount> accounts)
    {
        if (accounts.Count == 0 || accounts.Any(a => a.Code.Length == 0 || a.Code.Length > Account.CodeMaxLength || a.ParentCode.Length > Account.CodeMaxLength || a.Description.Length == 0 || a.Description.Length > 200))
            throw new InvalidOperationException("El catálogo debe contener cuentas de hasta 7 caracteres, incluido el punto, y descripciones válidas.");
        if (accounts.Select(a => a.Code).Distinct().Count() != accounts.Count) throw new InvalidOperationException("El catálogo contiene códigos repetidos.");
        var map = accounts.ToDictionary(a => a.Code);
        foreach (var account in accounts)
        {
            var seen = new HashSet<string>(); var current = account;
            while (true)
            {
                if (!seen.Add(current.Code) || seen.Count > 9) throw new InvalidOperationException("Jerarquía circular o mayor de 9 niveles.");
                if (current.ParentCode.Length == 0) break;
                if (!map.TryGetValue(current.ParentCode, out current!)) throw new InvalidOperationException("Falta una cuenta superior en el catálogo.");
            }
        }
    }
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
            RequireSession();
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
            RequireSession();
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

    // Empresas y Usuarios (master only): every user with the company it belongs to.
    public async Task<List<UserInfo>> UserList()
    {
        RequireMaster();
        var companies = (await db.Table<Company>().ToListAsync()).ToDictionary(c => c.Id, c => c.Name);
        return (await db.Table<LocalUser>().ToListAsync())
            .Select(u => new UserInfo(u.Id, u.Username, u.IsMaster, u.CompanyId, u.IsMaster ? "Maestro" : companies.GetValueOrDefault(u.CompanyId, "?"), u.Disabled))
            .OrderByDescending(u => u.IsMaster).ThenBy(u => u.CompanyName).ThenBy(u => u.Username).ToList();
    }

    // The master sets a new password for a user (e.g. a forgotten one); the user's remembered sessions end.
    public async Task SetUserPassword(int userId, string newPassword)
    {
        RequireMaster();
        var user = await db.FindAsync<LocalUser>(userId) ?? throw new InvalidOperationException("Usuario no disponible.");
        await ResetPasswordCore(user.Username, newPassword, true);
    }

    public async Task SetUserActive(int userId, bool active)
    {
        RequireMaster();
        await db.RunInTransactionAsync(c =>
        {
            RequireMaster();
            var user = c.Find<LocalUser>(userId) ?? throw new InvalidOperationException("Usuario no disponible.");
            if (user.IsMaster) throw new InvalidOperationException("El maestro no se puede suspender.");
            c.Execute("UPDATE LocalUser SET Disabled = ?, SecurityVersion = COALESCE(SecurityVersion, 0) + 1 WHERE Id = ?", !active, userId);
            c.Execute("DELETE FROM SessionToken WHERE UserId = ?", userId);
        });
    }

    // Removes an operator. The master cannot be removed; the company and its accounting stay.
    public async Task DeleteUser(int userId)
    {
        RequireMaster();
        await db.RunInTransactionAsync(c =>
        {
            RequireSession();
            var user = c.Find<LocalUser>(userId) ?? throw new InvalidOperationException("Usuario no disponible.");
            if (user.IsMaster) throw new InvalidOperationException("El usuario maestro no se puede borrar.");
            c.Execute("DELETE FROM SessionToken WHERE UserId = ?", userId);
            c.Delete(user);
        });
    }

    // Usuarios Conectados: users with a remembered session (signed in and not signed out).
    public async Task<List<SessionInfo>> ActiveSessions()
    {
        RequireMaster();
        var users = (await db.Table<LocalUser>().ToListAsync()).ToDictionary(u => u.Id);
        return (await db.Table<SessionToken>().ToListAsync())
            .Where(t => users.TryGetValue(t.UserId, out var u) && !u.Disabled && u.SecurityVersion == t.SecurityVersion && t.ExpiresAtUtc > clock.GetUtcNow().UtcDateTime && clock.GetUtcNow().UtcDateTime - t.LastUsedAt < IdleTimeout)
            .Select(t => new SessionInfo(users[t.UserId].Username, users[t.UserId].IsMaster, DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc).ToLocalTime(), DateTime.SpecifyKind(t.LastUsedAt, DateTimeKind.Utc).ToLocalTime()))
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




