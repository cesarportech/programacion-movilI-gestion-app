using GestionLibros.Data;
using GestionLibros.Models;
static class CaptureReferenceChecks
{
 public static async Task Run()
 {
  void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS CAPTURE " + message); }
  async Task Denied(Func<Task> action, string message) { try { await action(); } catch (InvalidOperationException) { Check(true,message); return; } throw new Exception(message); }
  var db = new AppDatabase(Path.Combine(Path.GetTempPath(), $"fredi-reference-{Guid.NewGuid():N}.db3"));
  await db.Setup("master", "Master-reference-123"); var company = await TestSupport.NewCompany(db,"Master-reference-123","References");
  await db.AddAccount(company,"1","Cash",""); await db.AddAccount(company,"2","Capital","");
  var accounts=await db.Accounts(company);
  List<JournalLine> Lines() => [new() { AccountId=accounts[0].Id, DebitCents=100 },new() {AccountId=accounts[1].Id,CreditCents=100}];
  var date=new DateTime(2026,10,13);
  Task Save(string type,string reference) => db.SaveCapturedJournal(company,new Journal {Type=type,Reference=reference,Date=date,Concept="Previous concept"},Lines());
  Check(await db.NextJournalReference(company,"1",date)=="6100001","New month and type start with year-month and 0001");
  await Save("1","6104988");
  Check(await db.NextJournalReference(company,"1",date)=="6104989","6104988 increments to 6104989");
  await Save("2","6100020");
  Check(await db.NextJournalReference(company,"2",date)=="6100021" && await db.NextJournalReference(company,"1",date)=="6104989","Policy types have independent sequences");
  Check(await db.NextJournalReference(company,"1",new(2026,11,1))=="6110001","Month resets sequence and prefix");
  Check(await db.NextJournalReference(company,"1",new(2027,1,1))=="7010001","Year uses its last digit");
  await Denied(()=>Save("1","61049888"),"Eighth digit rejected by capture service");
  await Denied(()=>Save("1","610ABCD"),"Non-numeric capture rejected");
  await Denied(()=>Save("1","6110001"),"Reference must match capture month");
  await Save("1","6104989");
  await Denied(()=>Save("1","6104989"),"Concurrent duplicate proposal cannot post twice");
  await Save("1","6104987");
  Check(await db.NextJournalReference(company,"1",date)=="6104990","After manual suffix, skips occupied references");
  Check((await db.PreviousJournalCapture(company))?.Reference=="6104987","F12 source is the last saved capture");
  await Save("3","6109999");
  await Denied(()=>db.NextJournalReference(company,"3",date),"Exhausted sequence never creates an eighth digit");
  await TestSupport.NewCompany(db,"Master-reference-123","Other company");
  await Denied(()=>db.NextJournalReference(company,"1",date),"Reference proposals preserve company isolation");
  await Denied(()=>db.PreviousJournalCapture(company),"F12 history preserves company isolation");
 }
}
