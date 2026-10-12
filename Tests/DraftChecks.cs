using GestionLibros.Data;
using GestionLibros.Models;
using System.Text.Json;
static class DraftChecks
{
 public static async Task Run()
 {
  int count = 0;
  void Check(bool ok, string text) { if (!ok) throw new Exception(text); count++; Console.WriteLine("PASS DRAFT " + text); }
  async Task Denied(Func<Task> work, string text) { try { await work(); } catch (InvalidOperationException) { Check(true, text); return; } throw new Exception(text); }
  var path = Path.Combine(Path.GetTempPath(), $"fredi-drafts-{Guid.NewGuid():N}.db3");
  var db = new AppDatabase(path); await db.Setup("master", "Master-drafts-123");
  var company = await TestSupport.NewCompany(db, "Master-drafts-123", "Draft company");
  await db.AddAccount(company, "1", "Cash", ""); await db.AddAccount(company, "2", "Capital", "");
  var accounts = await db.Accounts(company);
  var header = new Journal { Date = new(2026, 10, 7), Type = "01", Reference = "D-1", Concept = "Draft" };
  var rows = new List<JournalLine> { new() { AccountId = accounts[0].Id, DebitCents = 100 } };
  var raw = new MovementDraft("2", "Unfinished", "100+", -1);
  db.StoreJournalDraft("one", company, new(header, rows, raw));
  Check((await db.Journals(company, 2026, 10)).Count == 0, "Draft creates no posted journal");
  Check((await db.Balances(company, 2026, 10)).All(x => x.Closing == 0), "Unbalanced draft cannot affect balances");
  var reopened = new AppDatabase(path); await TestSupport.LoginAs(reopened, company);
  var recovered = JsonSerializer.Deserialize<JournalDraftContent>(reopened.JournalDrafts(company, 0).Single().Payload)!;
  Check(recovered.Movement == raw && recovered.Lines[0].DebitCents == 100 && recovered.Header.Reference == "D-1", "New instance recovers header, lines and incomplete movement text");
  await Denied(() => reopened.SaveJournal(company, recovered.Header, recovered.Lines, "one"), "Invalid draft cannot be accepted");
  Check(reopened.JournalDrafts(company, 0).Count == 1, "Failed acceptance preserves draft");
  recovered.Lines.Add(new() { AccountId = accounts[1].Id, CreditCents = 100 });
  reopened.StoreJournalDraft("one", company, recovered with { Movement = null });
  await reopened.SaveJournal(company, recovered.Header, recovered.Lines, "one");
  Check(reopened.JournalDrafts(company, 0).Count == 0, "Successful acceptance removes draft atomically");
  Check((await reopened.Balances(company, 2026, 10)).Single(x => x.Code == "1").Closing == 100, "Only accepted journal affects balances");
  Check(recovered.Header.Id > 0, "Saved editor receives journal identity");
  await Denied(() => reopened.SaveJournal(company, recovered.Header, recovered.Lines, "one"), "Already accepted draft cannot post twice");
  var saved = (await reopened.Journals(company, 2026, 10)).Single();
  reopened.StoreJournalDraft("edit", company, new(saved, recovered.Lines, null));
  var stale = JsonSerializer.Deserialize<JournalDraftContent>(reopened.JournalDrafts(company, saved.Id).Single().Payload)!;
  saved.Concept = "Changed in another window";
  await db.SaveJournal(company, saved, recovered.Lines);
  await Denied(() => reopened.SaveJournal(company, stale.Header, stale.Lines, "edit"), "Recovery cannot overwrite a subsequently changed journal");
  Check(reopened.JournalDrafts(company, saved.Id).Count == 1, "Conflict leaves draft recoverable");
  reopened.DeleteJournalDraft("edit", company);
  Check(reopened.JournalDrafts(company, saved.Id).Count == 0 && (await reopened.Journals(company, 2026, 10)).Count == 1, "Discard deletes only draft, not posted journal");
  var foreign = await TestSupport.NewCompany(db, "Master-drafts-123", "Other drafts");
  await Denied(() => Task.Run(() => db.JournalDrafts(company, 0)), "Another company cannot read drafts");
  reopened.StoreJournalDraft("private", company, new(header, rows, raw));
  await db.Login("master", "Master-drafts-123"); await db.AddUser("second", "Second-drafts-123", company);
  var second = new AppDatabase(path); await second.Login("second", "Second-drafts-123");
  Check(second.JournalDrafts(company, 0).Count == 0, "Another user in the same company cannot recover private drafts");
  await Denied(() => Task.Run(() => second.StoreJournalDraft("private", company, new(header, rows, null))), "Another user cannot overwrite a private draft");
  second.DeleteJournalDraft("private", company);
  Check(reopened.JournalDrafts(company, 0).Count == 1, "Another user cannot discard a private draft");
  await db.SetUserPassword(reopened.Session!.UserId, "Updated-drafts-123");
  await Denied(() => Task.Run(() => reopened.StoreJournalDraft("private", company, new(header, rows, null))), "Revoked session cannot write drafts");
  await reopened.Login(TestSupport.UserFor(company), "Updated-drafts-123");
  Check(reopened.JournalDrafts(company, 0).Count == 1, "Security lock preserves existing drafts for authorized recovery");
  Console.WriteLine($"All {count} draft checks passed.");
 }
}
