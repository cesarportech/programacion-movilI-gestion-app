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
public record BalanceRow(string Code, string Description, int Level, long Opening, long Debits, long Credits)
{
 public long Closing => checked(Opening + Debits - Credits);
}
