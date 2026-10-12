using GestionLibros.Data;
using GestionLibros.Data.Import;
using GestionLibros.Models;
using GestionLibros.Reporting;
using SQLite;
static class MigrationChecks
{
 public static async Task Run()
 {
  int count = 0;
  void Check(bool ok, string text) { if (!ok) throw new Exception(text); count++; Console.WriteLine("PASS MIGRATION " + text); }
  async Task Denied(Func<Task> work, string text) { try { await work(); } catch (InvalidOperationException) { Check(true, text); return; } throw new Exception("Expected rejection: " + text); }
  var path = Path.Combine(Path.GetTempPath(), $"fredi-migration-{Guid.NewGuid():N}.db3");
  var db = new AppDatabase(path); await db.Setup("master", "Migration-test-123");
  var company = await TestSupport.NewCompany(db, "Migration-test-123", "Migration");
  GlBalances Gl(long opening, long debit, long credit) { var d = new long[12]; var c = new long[12]; d[0] = debit; c[0] = credit; return new(opening, d, c); }
  List<GlAccount> Catalog(long opening = 5000, long debit = 10000) => [
   new("1", "", 0, "Assets", null, "", "", Gl(opening, debit, 0)),
   new("1.1", "1", 1, "Cash", null, "", "", Gl(opening, debit, 0)),
   new("2", "", 0, "Capital", null, "", "", Gl(-opening, 0, debit))];
  await Denied(() => db.ImportAccounts(company, Catalog(), 2026), "Financial imports require explicit cutoff");
  Check((await db.Accounts(company)).Count == 0, "Missing cutoff leaves catalog unchanged");
  await Denied(() => db.SetMigrationCutoff(company, new(2026, 1, 15)), "Monthly source rejects partial-month cutoff");
  await db.SetMigrationCutoff(company, new(2026, 1, 31));
  await db.ImportAccounts(company, Catalog(), 2026, "cat-a.tps");
  var accounts = (await db.Accounts(company)).ToDictionary(a => a.Code);
  async Task Save(DateTime date, string reference) => await db.SaveJournal(company, new Journal { Date = date, Type = "01", Reference = reference, Concept = "Local" },
   [new() { AccountId = accounts["1.1"].Id, DebitCents = 2000 }, new() { AccountId = accounts["2"].Id, CreditCents = 2000 }]);
  await Denied(() => Save(new(2026, 1, 31), "overlap"), "Local entry on cutoff rejected");
  await Save(new(2026, 2, 1), "new");
  Check((await db.Balances(company, 2026, 2)).Single(b => b.Code == "1.1").Closing == 17000, "FREDI starts next day without duplication");
  await db.ImportAccounts(company, Catalog().AsEnumerable().Reverse().ToList(), 2026, "same-renamed.tps");
  Check((await db.MigrationBatches(company)).Count == 1, "Repeated renamed/reordered aggregate is idempotent");
  await Denied(() => db.SetMigrationCutoff(company, new(2026, 2, 28)), "Cannot silently move cut after import");
  await Denied(() => db.ImportAccounts(company, Catalog(), 2027), "Future exercise rejected");
  var future = Catalog(); future[1].Balances!.Debits[1] = 1;
  await Denied(() => db.ImportAccounts(company, future, 2026), "Post-cut activity in annual file rejected");
  await Denied(() => db.ImportAccounts(company, [Catalog()[0]], 2026), "Partial replacement catalog rejected");
  MigrationJournal Policy(string id, int day, long amount) => new(id, "01", id, new(2026, 1, day), "Historic", [new("1.1", "Cash", amount, 0), new("2", "Capital", 0, amount)]);
  var details = new MigrationDetailFile(2026, 1, [Policy("old-a", 10, 6000), Policy("old-b", 20, 4000)]);
  await Denied(() => db.ImportHistoricalDetails(company, details with { Journals = [Policy("bad", 10, 9000)] }, "bad.json"), "Incomplete detail cannot replace aggregate");
  Check((await db.Journals(company, 2026, 1)).Count == 0 && (await db.MigrationBatches(company)).Count == 1, "Failed reconciliation rolls back journals and batch");
  await Denied(() => db.ImportAccounts(company, Catalog().Select(a => a with { Balances = null }).ToList(), 2026), "Missing amounts never erase known balances as zero");
  var before = await db.Balances(company, 2026, 1);
  var detailBatch = await db.ImportHistoricalDetails(company, details, "detail.json");
  var after = await db.Balances(company, 2026, 1);
  Check((await db.MigrationBatches(company)).Single(b => b.Id == detailBatch).ReplacesBatchIds.Length > 0, "Detail batch records which aggregate it replaces");
  Check(before.SequenceEqual(after), "Every account gross amounts and balances unchanged after detail replacement");
  Check((await db.Balances(company, 2026, 2)).Single(b => b.Code == "1.1").Closing == 17000, "Next period opening excludes replaced aggregate");
  var ledger = await db.Ledger(company, accounts["1.1"].Id, new(2026, 1, 1), new(2026, 1, 31));
  Check(ledger.Rows.Count == 2 && ledger.Rows.All(r => !r.IsImported) && ledger.Debits == 10000 && ledger.Closing == 15000, "Mayor displays detailed journals once, not the aggregate");
  Check((await db.Ledger(company, accounts["1.1"].Id, new(2026, 1, 1), new(2026, 1, 15))).Closing == 11000, "Partial dates become exact once detail exists");
  Check((await db.Ledger(company, accounts["1"].Id, new(2026, 1, 1), new(2026, 1, 31), false)).Debits == 10000, "Direct parent retains own historical aggregate without double-counting descendants");
  Check((await db.JournalForMovement(company, ledger.Rows[0].JournalId, ledger.Rows[0].LineId)).ImportBatchId == detailBatch, "Historical drilldown preserves source batch");
   Check(ReportFormatter.Csv(ReportFormatter.Ledger(ledger)).Contains(detailBatch), "Export identifies historical detail batch");
  var replay = await db.ImportHistoricalDetails(company, details with { Journals = details.Journals.AsEnumerable().Reverse().ToList() }, "renamed.json");
  Check(replay == detailBatch && (await db.Journals(company, 2026, 1)).Count == 2, "Replayed reordered detail is idempotent");
  await Denied(() => db.ImportHistoricalDetails(company, details with { Journals = [Policy("other", 10, 10000)] }, "different.json"), "Different detail cannot append to replaced period");
  await db.ImportAccounts(company, Catalog(), 2026);
  Check((await db.Balances(company, 2026, 1)).Single(b => b.Code == "1.1").Closing == 15000, "Aggregate replay after detail does not reactivate aggregate");
  await Denied(() => db.ImportAccounts(company, Catalog(5000, 10001), 2026), "Changed aggregate cannot overwrite detailed exercise");
  var historical = (await db.Journals(company, 2026, 1)).First();
  await Denied(() => db.DeleteJournal(company, historical.Id), "Historical detail cannot be individually deleted");
  historical.Date = new(2026, 2, 2);
  await Denied(async () => await db.SaveJournal(company, historical, await db.Lines(company, historical.Id)), "Historical detail cannot become local by editing its date");
  var batches = await db.MigrationBatches(company);
  Check(batches.All(b => b.Payload.Length > 0 && b.Fingerprint.Length == 64 && b.ImportedBy.Length > 0) && batches.Any(b => b.Source == "cat-a.tps"), "Audit records source, payload, hash, period and operator");
  var other = await TestSupport.NewCompany(db, "Migration-test-123", "Next exercise");
  await db.SetMigrationCutoff(other, new(2027, 1, 31));
  await db.ImportAccounts(other, Catalog(), 2026);
  await db.ImportHistoricalDetails(other, details, "old.json");
  await db.ImportAccounts(other, Catalog(15000, 100), 2027);
  Check((await db.Balances(other, 2027, 1)).Single(b => b.Code == "1.1").Closing == 15100, "Next annual opening already includes earlier imported detail");
  await db.ImportAccounts(other, Catalog(0, 0), 2027);
  Check((await db.MigrationBatches(other)).Any(b => b.SupersededBy.Length > 0), "Replaced aggregate batch remains archived with successor");
  Check((await db.Balances(other, 2027, 1)).Single(b => b.Code == "1.1").Closing == 0, "Explicit zero annual snapshot does not resurrect prior amounts or detail");
  await Denied(() => db.ImportAccounts(company, Catalog(), 2026), "Company boundary applies to import");
  var legacy = await TestSupport.NewCompany(db, "Migration-test-123", "Legacy unresolved");
  await db.ImportAccounts(legacy, Catalog());
  var legacyAccounts = (await db.Accounts(legacy)).ToDictionary(a => a.Code);
  var sql = new SQLiteAsyncConnection(path);
  await sql.InsertAsync(new ImportedBalance { CompanyId = legacy, AccountId = legacyAccounts["1.1"].Id, Year = 2026, Month = 0, DebitCents = 5000 });
  await Denied(() => db.Balances(legacy, 2026, 1), "Unconfigured old imports fail closed instead of displaying ambiguous balances");
  await db.SetMigrationCutoff(legacy, new(2026, 1, 31));
  Check((await db.MigrationBatches(legacy)).Single().Source.Contains("no registrado"), "Legacy provenance explicitly marked unknown, not invented");
  Check((await db.Balances(legacy, 2026, 1)).Single(b => b.Code == "1.1").Closing == 5000, "Legacy balance preserved after nonoverlapping cutoff");
  var conflict = await TestSupport.NewCompany(db, "Migration-test-123", "Existing local overlap");
  await db.AddAccount(conflict, "1", "Cash", ""); await db.AddAccount(conflict, "2", "Capital", "");
  var ca = await db.Accounts(conflict);
  await db.SaveJournal(conflict, new Journal { Type = "01", Reference = "old-local", Concept = "Local", Date = new(2026, 1, 10) }, [new() { AccountId = ca[0].Id, DebitCents = 100 }, new() { AccountId = ca[1].Id, CreditCents = 100 }]);
  await Denied(() => db.SetMigrationCutoff(conflict, new(2026, 1, 31)), "Existing local overlap prevents selecting ambiguous cutoff");
  Check(await db.MigrationCutoff(conflict) == null && (await db.Journals(conflict, 2026, 1)).Count == 1, "Rejected cutoff leaves original data untouched");
  await sql.InsertAsync(new ImportedBalance { CompanyId = conflict, AccountId = ca[0].Id, Year = 2026, Month = 1, DebitCents = 100 });
  Check(await db.ConsultationNeedsSource(conflict), "Unconfigured mixed data requests explicit source");
  await Denied(() => db.Balances(conflict, 2026, 1), "Default reports still reject unreconciled sources");
  foreach (var source in new[] { AppDatabase.ConsultationSource.ImportedOnly, AppDatabase.ConsultationSource.LocalOnly })
  {
   var balance = (await db.Balances(conflict, 2026, 1, source)).Single(b => b.Code == ca[0].Code);
   var breakdown = await db.Ledger(conflict, ca[0].Id, new(2026, 1, 1), new(2026, 1, 31), source: source);
   Check(balance.Debits == 100 && breakdown.Debits == 100 && balance.Closing == breakdown.Closing, $"{source} balances and popup agree without double counting");
   Check(breakdown.Rows.Count == 1 && breakdown.Rows[0].IsImported == (source == AppDatabase.ConsultationSource.ImportedOnly), $"{source} keeps detail distinct from aggregates");
   if (source == AppDatabase.ConsultationSource.LocalOnly)
    Check((await db.JournalForMovement(conflict, breakdown.Rows[0].JournalId, breakdown.Rows[0].LineId)).Reference == "old-local", "Unreconciled local detail opens exact existing journal");
  }
  Check(await db.MigrationCutoff(conflict) == null, "Read-only consultation never invents a migration cutoff");
  await Denied(() => db.Balances(company, 2026, 1, AppDatabase.ConsultationSource.ImportedOnly), "Source consultation respects company isolation");
  Console.WriteLine($"All {count} migration checks passed.");
 }
}


