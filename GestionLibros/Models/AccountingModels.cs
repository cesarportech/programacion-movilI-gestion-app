using SQLite;
namespace GestionLibros.Models;
public class Company
{
 [PrimaryKey, AutoIncrement] public int Id { get; set; }
 [Unique] public string Name { get; set; } = "";
 public override string ToString() => Name;
}
public class LocalUser
{
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
public record SessionInfo(string Username, bool IsMaster, DateTime CreatedAt, DateTime LastUsedAt);
// One movement of a printed póliza, with its account resolved.
public record PolicyPrintLine(string AccountCode, string AccountName, string Concept, long Debits, long Credits);
public record PolicyPrint(Journal Journal, string TypeName, IReadOnlyList<PolicyPrintLine> Lines);
// Remembered sign-in. Only the SHA-256 of the token is stored; the token itself stays on the device.
public class SessionToken
{
 [PrimaryKey, AutoIncrement] public int Id { get; set; }
 [Indexed] public int UserId { get; set; }
 [Unique] public string TokenHash { get; set; } = "";
 public DateTime CreatedAt { get; set; }
 public DateTime LastUsedAt { get; set; }
}
public record UserSession(int UserId, string Username, bool IsMaster, int CompanyId);

public class Journal
{
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
