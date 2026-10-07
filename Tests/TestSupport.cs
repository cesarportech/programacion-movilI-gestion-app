using GestionLibros.Data;

// The master only administers companies and users; accounting work is done by each company's operator.
static class TestSupport
{
    public const string OperatorPassword = "Operador-prueba-123";

    public static string UserFor(int companyId) => $"op{companyId}";

    // As master: creates the company and its operator, then signs in as that operator.
    public static async Task<int> NewCompany(AppDatabase db, string masterPassword, string name)
    {
        await db.Login("master", masterPassword);
        await db.AddCompany(name);
        var id = (await db.Companies()).Single(c => c.Name == name).Id;
        await db.AddUser(UserFor(id), OperatorPassword, id);
        await LoginAs(db, id);
        return id;
    }

    public static Task LoginAs(AppDatabase db, int companyId) => db.Login(UserFor(companyId), OperatorPassword);
}
