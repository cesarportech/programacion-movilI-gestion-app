using GestionLibros.Models;
using SQLite;
using System.Security.Cryptography;
namespace GestionLibros.Data;
public sealed partial class AppDatabase
{
 private readonly SQLiteAsyncConnection db;
 private readonly SemaphoreSlim gate = new(1, 1);
 private bool ready;
 private UserSession? session;
 public UserSession? Session => session;
 public AppDatabase(string path) => db = new SQLiteAsyncConnection(path);
 private async Task Initialize()
 {
  await gate.WaitAsync();
  try {
   if (ready) return;
   await db.CreateTableAsync<Company>();
   await db.CreateTableAsync<LocalUser>();
   await db.CreateTableAsync<Account>();
   await db.CreateTableAsync<Journal>();
   await db.CreateTableAsync<JournalLine>();
   await db.CreateTableAsync<ImportedBalance>();
   await db.CreateTableAsync<SessionToken>();
   await db.CreateTableAsync<PolicyType>();
   await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_PolicyType_Code ON PolicyType (CompanyId, Code)");
   await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_ImportedBalance_Period ON ImportedBalance (AccountId, Year, Month)");
   await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Journal_Reference ON Journal (CompanyId, Year, Type, Reference)");
   await db.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_Account_Company_Code ON Account (CompanyId, Code)");
   ready = true;
  } finally { gate.Release(); }
 }
 public async Task<bool> NeedsSetup() { await Initialize(); return await db.Table<LocalUser>().CountAsync() == 0; }
 private static LocalUser NewUser(string username, string password, bool master, int companyId)
 {
  username = username.Trim().ToLowerInvariant();
  if (username.Length < 3 || username.Length > 60) throw new InvalidOperationException("El usuario debe tener entre 3 y 60 caracteres.");
  if (password.Length < 10) throw new InvalidOperationException("La contraseña debe tener al menos 10 caracteres.");
  var salt = RandomNumberGenerator.GetBytes(16);
  return new LocalUser { Username = username, Salt = Convert.ToBase64String(salt), PasswordHash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32)), IsMaster = master, CompanyId = companyId };
 }
 public async Task Setup(string username, string password)
 {
  await Initialize();
  var user = NewUser(username, password, true, 0);
  await db.RunInTransactionAsync(c => {
   if (c.Table<LocalUser>().Count() != 0) throw new InvalidOperationException("La configuración inicial ya se realizó.");
   c.Insert(user);
  });
 }
 public async Task Login(string username, string password)
 {
  session = null; tokenHash = null;
  await Initialize();
  username = username.Trim().ToLowerInvariant();
  var user = await db.Table<LocalUser>().Where(u => u.Username == username).FirstOrDefaultAsync();
  if (user == null) throw new InvalidOperationException("Usuario o contraseña incorrectos.");
  var hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(user.Salt), 210000, HashAlgorithmName.SHA256, 32);
  if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(user.PasswordHash))) throw new InvalidOperationException("Usuario o contraseña incorrectos.");
  session = new(user.Id, user.Username, user.IsMaster, user.CompanyId);
 }
 // Clears the in-memory session only; SignOut also revokes the remembered token.
 public void Logout() { session = null; tokenHash = null; }
 private UserSession RequireSession() => session ?? throw new InvalidOperationException("Debe iniciar sesión.");
 private void RequireMaster() { if (!RequireSession().IsMaster) throw new InvalidOperationException("Solo el usuario maestro puede administrar usuarios y contabilidades."); }
 // Each accounting is only reachable by the user assigned to it. The master administers companies
 // and users but never opens an accounting: to see one, sign in with that company's user.
 private void RequireCompany(int companyId)
 {
  var user = RequireSession();
  if (user.IsMaster) throw new InvalidOperationException("El usuario maestro solo administra empresas y usuarios. Para ver una contabilidad, entre con el usuario asignado a ella.");
  if (companyId <= 0 || user.CompanyId != companyId) throw new InvalidOperationException("No tiene acceso a esta contabilidad.");
 }
 public async Task<List<Company>> Companies()
 {
  var user = RequireSession();
  return user.IsMaster ? await db.Table<Company>().OrderBy(c => c.Name).ToListAsync() : await db.Table<Company>().Where(c => c.Id == user.CompanyId).ToListAsync();
 }
 public async Task AddCompany(string name)
 {
  RequireMaster(); name = name.Trim();
  if (name.Length == 0 || name.Length > 120) throw new InvalidOperationException("Escriba un nombre de contabilidad de hasta 120 caracteres.");
  if (await db.Table<Company>().Where(c => c.Name == name).CountAsync() > 0) throw new InvalidOperationException("Esta contabilidad ya existe.");
  await db.InsertAsync(new Company { Name = name });
 }
 public async Task AddUser(string username, string password, int companyId)
 {
  RequireMaster();
  if (await db.FindAsync<Company>(companyId) == null) throw new InvalidOperationException("Seleccione una contabilidad válida.");
  var user = NewUser(username, password, false, companyId);
  if (await db.Table<LocalUser>().Where(u => u.Username == user.Username).CountAsync() > 0) throw new InvalidOperationException("Este usuario ya existe.");
  await db.InsertAsync(user);
 }
 public async Task<List<string>> Users()
 {
  RequireMaster();
  var users = await db.Table<LocalUser>().ToListAsync();
  var companies = await Companies();
  return users.Select(u => $"{u.Username} — {(u.IsMaster ? "Maestro" : companies.FirstOrDefault(c => c.Id == u.CompanyId)?.Name)}").ToList();
 }
 public Task<List<Account>> Accounts(int companyId)
 {
  RequireCompany(companyId);
  return db.Table<Account>().Where(a => a.CompanyId == companyId).OrderBy(a => a.Code).ToListAsync();
 }
 public async Task AddAccount(int companyId, string code, string description, string parentCode)
 {
  RequireCompany(companyId);
  if (await db.FindAsync<Company>(companyId) == null) throw new InvalidOperationException("La contabilidad no existe.");
  code = code.Trim(); description = description.Trim(); parentCode = parentCode.Trim();
  if (code.Length == 0 || code.Length > 40 || description.Length == 0 || description.Length > 200) throw new InvalidOperationException("Indique código (máximo 40) y descripción (máximo 200).");
  var accounts = await Accounts(companyId);
  if (accounts.Any(a => a.Code == code)) throw new InvalidOperationException("El código ya existe en esta contabilidad.");
  var parent = accounts.FirstOrDefault(a => a.Code == parentCode);
  if (parentCode.Length > 0 && parent == null) throw new InvalidOperationException("La cuenta superior debe existir en esta contabilidad.");
  if (parent != null && await db.Table<JournalLine>().Where(l => l.AccountId == parent.Id).CountAsync() > 0) throw new InvalidOperationException("No puede agregar subcuentas a una cuenta con movimientos.");
  var level = parent == null ? 1 : parent.Level + 1;
  if (level > 9) throw new InvalidOperationException("El catálogo admite hasta 9 niveles en esta versión de prueba.");
  await db.InsertAsync(new Account { CompanyId = companyId, Code = code, Description = description, ParentCode = parentCode, Level = level, ChangedBy = RequireSession().Username, ChangedAt = DateTime.Now });
 }
}
