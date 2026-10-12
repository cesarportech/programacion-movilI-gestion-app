using GestionLibros.Data;
using GestionLibros.Models;

static class DrilldownChecks
{
 public static async Task Run()
 {
  void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS DRILLDOWN " + message); }
  async Task Denied(Func<Task> action, string message) { try { await action(); } catch (InvalidOperationException) { Check(true, message); return; } throw new Exception(message); }
  var db = new AppDatabase(Path.Combine(Path.GetTempPath(), $"fredi-drilldown-{Guid.NewGuid():N}.db3"));
  await db.Setup("master", "Master-drilldown-123");
  var company = await TestSupport.NewCompany(db, "Master-drilldown-123", "Drilldown");
  await db.AddAccount(company, "1", "Cash", ""); await db.AddAccount(company, "2", "Capital", "");
  var accounts = await db.Accounts(company);
  List<JournalLine> Lines() => [new() { AccountId = accounts[0].Id, DebitCents = 100 }, new() { AccountId = accounts[1].Id, CreditCents = 100 }];
  var first = new Journal { Date = new(2026, 10, 1), Type = "01", Reference = "SAME", Concept = "First" };
  var other = new Journal { Date = new(2025, 10, 1), Type = "01", Reference = "SAME", Concept = "Other year" };
  await db.SaveJournal(company, first, Lines()); await db.SaveJournal(company, other, Lines());
  var ledger = await db.Ledger(company, accounts[0].Id, new(2026, 10, 1), new(2026, 10, 31));
  var row = ledger.Rows.Single();
  var journal = await db.JournalForMovement(company, row.JournalId, row.LineId);
  Check(journal.Id == first.Id && journal.Year == 2026, "Duplicate reference resolves exact journal identity and year");
  Check((await db.Lines(company, journal.Id)).Count == 2, "Opening journal includes counterpart movement");
  await Denied(() => db.JournalForMovement(company, other.Id, row.LineId), "Mismatched movement and journal rejected");
  await Denied(() => db.JournalForMovement(company, 0, 0), "Aggregate cannot masquerade as journal");
  Check(db.JournalDrafts(company, journal.Id).Count == 0, "Reading movement does not create draft");
  await db.DeleteJournal(company, first.Id);
  await Denied(() => db.JournalForMovement(company, row.JournalId, row.LineId), "Deleted movement rejected instead of opening another journal");
  var foreignLineId = (await db.Lines(company, other.Id)).First().Id;
  await TestSupport.NewCompany(db, "Master-drilldown-123", "Foreign");
  await Denied(() => db.JournalForMovement(company, other.Id, foreignLineId), "Foreign company cannot access journal");
 }
}

