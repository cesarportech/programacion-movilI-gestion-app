using SQLite;
namespace GestionLibros.Models;
public class Company
{
 public int CatalogPackageId { get; set; }
 [PrimaryKey, AutoIncrement] public int Id { get; set; }
 [Unique] public string Name { get; set; } = "";
 public override string ToString() => Name;
}
// Immutable structural catalog. Deliberately has no balances, journal IDs or company data.
public class CatalogPackage
{
 [PrimaryKey, AutoIncrement] public int Id { get; set; }
 [Unique] public string Name { get; set; } = "";
 public string Payload { get; set; } = "";
 public string CreatedBy { get; set; } = "";
 public DateTime CreatedUtc { get; set; }
 public override string ToString() => Name;
}
public record CatalogAccount(string Code, string ParentCode, string Description);
public class LocalUser
{
 public bool Disabled { get; set; }
 public long SecurityVersion { get; set; }
 [PrimaryKey, AutoIncrement] public int Id { get; set; }
 [Unique] public string Username { get; set; } = "";
 public string PasswordHash { get; set; } = "";
 public string Salt { get; set; } = "";
 public bool IsMaster { get; set; }
 public int CompanyId { get; set; }
}
public class Account
{
 [PrimaryKey, AutoIncrement] public int Id { get; set; }
 [Indexed] public int CompanyId { get; set; }
 public string Code { get; set; } = "";
 public string ParentCode { get; set; } = "";
 public int Level { get; set; }
 public string Description { get; set; } = "";
 public string ChangedBy { get; set; } = "";
 public DateTime ChangedAt { get; set; }
 // "C" = changed after creation, as the GL2000 catalog marks it.
 public string Status { get; set; } = "";
}
// Catálogo de Tipos de Pólizas (e.g. 01 = INGRESOS), per company.
public class PolicyType
{
 [PrimaryKey, AutoIncrement] public int Id { get; set; }
 [Indexed] public int CompanyId { get; set; }
 public string Code { get; set; } = "";
 public string Description { get; set; } = "";
}
public record UserInfo(int Id, string Username, bool IsMaster, int CompanyId, string CompanyName, bool Disabled = false)
{ public string DisplayUsername => Username + (Disabled ? " (Suspendido)" : ""); }
public record SessionInfo(string Username, bool IsMaster, DateTime CreatedAt, DateTime LastUsedAt);
// One movement of a printed póliza, with its account resolved.
public record PolicyPrintLine(string AccountCode, string AccountName, string Concept, long Debits, long Credits);
public record PolicyPrint(Journal Journal, string TypeName, IReadOnlyList<PolicyPrintLine> Lines);
// Remembered sign-in. Only the SHA-256 of the token is stored; the token itself stays on the device.
public class SessionToken
{
 public long SecurityVersion { get; set; }
 public DateTime ExpiresAtUtc { get; set; }
 [PrimaryKey, AutoIncrement] public int Id { get; set; }
 [Indexed] public int UserId { get; set; }
 [Unique] public string TokenHash { get; set; } = "";
 public DateTime CreatedAt { get; set; }
 public DateTime LastUsedAt { get; set; }
}
public record UserSession(int UserId, string Username, bool IsMaster, int CompanyId);

public class Journal
{
 public string ImportBatchId { get; set; } = "";
 public string SourceId { get; set; } = "";
 [PrimaryKey, AutoIncrement] public int Id { get; set; }
 [Indexed] public int CompanyId { get; set; }
 public int Year { get; set; }
 public int Month { get; set; }
 public DateTime Date { get; set; }
 public string Type { get; set; } = "";
 public string Reference { get; set; } = "";
 public string Concept { get; set; } = "";
 public string ChangedBy { get; set; } = "";
 public DateTime ChangedAt { get; set; }
}
public class JournalLine
{
 [PrimaryKey, AutoIncrement] public int Id { get; set; }
 [Indexed] public int JournalId { get; set; }
 [Indexed] public int AccountId { get; set; }
 public string Concept { get; set; } = "";
 public long DebitCents { get; set; }
 public long CreditCents { get; set; }
}
// Accumulated amounts copied from a GL2000 catalog for one detail account and year.
// Month 0 holds the year's opening balance (debit side positive, credit side in CreditCents).
public class ImportedBalance
{
 public string ImportBatchId { get; set; } = "";
 public bool RollupOnly { get; set; }
 [PrimaryKey, AutoIncrement] public int Id { get; set; }
 [Indexed] public int CompanyId { get; set; }
 [Indexed] public int AccountId { get; set; }
 public int Year { get; set; }
 public int Month { get; set; }
 public long DebitCents { get; set; }
 public long CreditCents { get; set; }
}
public record BalanceRow(string Code, string Description, int Level, long Opening, long Debits, long Credits, bool IsDetail = true)
{
 public long Closing => checked(Opening + Debits - Credits);
}
// Drafts are deliberately separate from Journal/JournalLine: reports never read them.
public class JournalDraft
{
 public long Revision { get; set; }
 [PrimaryKey] public string Id { get; set; } = "";
 public int UserId { get; set; }
 public int CompanyId { get; set; }
 public int JournalId { get; set; }
 public DateTime UpdatedUtc { get; set; }
 public string Payload { get; set; } = "";
}
public record MovementDraft(string Account, string Concept, string Value, int Index);
public record JournalDraftContent(Journal Header, List<JournalLine> Lines, MovementDraft? Movement);
public class MigrationPolicy
{
 [PrimaryKey] public int CompanyId { get; set; }
 public DateTime LegacyThrough { get; set; }
 public string SetBy { get; set; } = "";
 public DateTime SetAtUtc { get; set; }
}
public class ImportBatch
{
 public string SupersededBy { get; set; } = "";
 public string ReplacesBatchIds { get; set; } = "";
 [PrimaryKey] public string Id { get; set; } = "";
 [Indexed] public int CompanyId { get; set; }
 public string Kind { get; set; } = "";
 public string Source { get; set; } = "";
 public string Fingerprint { get; set; } = "";
 public int Year { get; set; }
 public int FromMonth { get; set; }
 public int ThroughMonth { get; set; }
 public DateTime ImportedUtc { get; set; }
 public string ImportedBy { get; set; } = "";
 public string Payload { get; set; } = "";
}
public class DetailedPeriod
{
 [PrimaryKey] public string Key { get; set; } = "";
 public int CompanyId { get; set; }
 public int Year { get; set; }
 public int Month { get; set; }
 public string BatchId { get; set; } = "";
}
public record MigrationLine(string AccountCode, string Concept, long DebitCents, long CreditCents);
public record MigrationJournal(string SourceId, string Type, string Reference, DateTime Date, string Concept, List<MigrationLine> Lines);
public record MigrationDetailFile(int Year, int Month, List<MigrationJournal> Journals);
