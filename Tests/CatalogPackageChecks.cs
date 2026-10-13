using GestionLibros.Data;
using GestionLibros.Models;
using SQLite;
static class CatalogPackageChecks
{
 public static async Task Run()
 {
  void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS CATALOG " + text); }
  async Task Denied(Func<Task> work, string text) { try { await work(); } catch (InvalidOperationException) { Check(true,text); return; } throw new Exception(text); }
  var path=Path.Combine(Path.GetTempPath(), $"fredi-packages-{Guid.NewGuid():N}.db3");
  var db=new AppDatabase(path); await db.Setup("master", "Master-packages-123"); await db.Login("master", "Master-packages-123");
  CatalogAccount[] accounts=[new("1","","Activo"),new("1.1","1","Caja")];
  await db.PreloadCatalog("Seven", [new("1.12101", "", "Cuenta válida")]);
  await Denied(()=>db.PreloadCatalog("Eight", [new("1.121012", "", "Cuenta larga")]),"Rejects eight characters including decimal point");
  var id=await db.PreloadCatalog("Base",accounts);
  Check((await db.CatalogPackages()).Single(p=>p.Name=="Base").Id==id,"Master preloads structural package");
  await Denied(()=>db.PreloadCatalog("Base",accounts),"Existing package cannot be overwritten");
  await Denied(()=>db.PreloadCatalog("Invalid",[new("1","2","A"),new("2","1","B")]),"Circular package rejected");
  var unused = await db.PreloadCatalog("Unused", accounts);
  await db.DeleteCatalogPackage(unused);
  Check(!(await db.CatalogPackages()).Any(p => p.Id == unused), "Unassigned catalog can be deleted");
  var prepared = await db.CreateCompanyFromCatalog("Prepared",id);
  await Denied(()=>db.DeleteCatalogPackage(id),"Assigned catalog cannot be deleted");
  var verify = new SQLiteAsyncConnection(path);
  Check((await verify.Table<Account>().Where(a=>a.CompanyId==prepared).ToListAsync()).Single(a=>a.Code=="1.1").Level==2,"Company copies chosen hierarchy atomically");
  Check((await verify.FindAsync<Company>(prepared)).CatalogPackageId==id,"Company permanently records master's catalog");
  await Denied(()=>db.CreateCompanyFromCatalog("Invalid package",99999),"Missing package cannot create company");
  Check(await verify.Table<Company>().Where(c=>c.Name=="Invalid package").CountAsync()==0,"Failed creation leaves no empty company");
  var company=await TestSupport.NewCompany(db,"Master-packages-123","Locked");
  await Denied(()=>db.DeleteCatalogPackage(id),"Operator cannot delete catalogs");
  await Denied(()=>db.CreateCompanyFromCatalog("Unauthorized",id),"Operator cannot choose company or catalog");
  await Denied(()=>db.PreloadCatalog("Operator",accounts),"Operator cannot preload catalog");
  await db.AddAccount(company,"1","Activo","");
  var account=(await db.Accounts(company)).Single();
  var sql=new SQLiteAsyncConnection(path); await sql.ExecuteAsync("UPDATE Company SET CatalogPackageId=? WHERE Id=?",id,company);
  await Denied(()=>db.AddAccount(company,"2","Other",""),"Locked catalog rejects adding accounts");
  await Denied(()=>db.UpdateAccount(company,account.Id,"Changed"),"Locked catalog rejects renaming accounts");
  await Denied(()=>db.DeleteAccount(company,account.Id),"Locked catalog rejects removing accounts");
  await Denied(()=>db.ImportAccounts(company,[new("2","",1,"New",null,"","")]),"Locked catalog rejects replacement import");
  Check((await db.Accounts(company)).Single().Description=="Activo","Rejected edits preserve accounts");
  Check((await db.Balances(company,2026,1)).All(b=>b.Closing==0),"Package adds no balance");
 }
}




