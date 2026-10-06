using GestionLibros.Data;
using GestionLibros.Models;

namespace GestionLibros.Views;

// "Agregando / Cambiando una Cuenta" window floating over the catalog, like the original child dialog.
internal sealed class AccountDialog : ClassicDialog
{
    private readonly AppDatabase db;
    private readonly Company company;
    private readonly Func<string, Task> saved;
    private readonly Entry code = new() { MaxLength = 40 };
    private readonly Entry parent = new() { MaxLength = 40 };
    private readonly Entry level = new() { IsReadOnly = true };
    private readonly Entry last = new() { IsReadOnly = true };
    private readonly Entry description = new() { MaxLength = 200 };
    private readonly View parentSearch;
    private List<Account> accounts = [];
    private Account? editing;

    public AccountDialog(Page page, AppDatabase db, Company company, Func<string, Task> saved)
        : base(page, 444, 156, "Cuenta",
            "Cuenta: número de la cuenta. Cuenta Superior: cuenta de la que depende (vacía para una cuenta de primer nivel); la lupa muestra las cuentas que coinciden. " +
            "Nivel y Ultimo Nivel se calculan solos. Al cambiar una cuenta solo se modifica la descripción.")
    {
        this.db = db; this.company = company; this.saved = saved;
        foreach (var (text, y) in new[] { ("Cuenta :", 8d), ("Cuenta Superior :", 33d), ("Nivel :", 57d), ("Ultimo Nivel :", 82d), ("Descripción :", 110d) })
            FieldLabel(text, y);
        Put(Field(code, Gl2000Chrome.RowYellow, "Cuenta"), 118, 8, 90, 20);
        Put(Field(parent, Gl2000Chrome.RowYellow, "Cuenta superior"), 118, 33, 90, 20);
        parentSearch = JournalsPage.ActionButton("", "gl_search.png", PickParent, 30);
        Put(parentSearch, 212, 31, 30, 24);
        Put(Field(level, Colors.White, "Nivel"), 118, 57, 28, 20);
        Put(Field(last, Colors.White, "Último nivel"), 118, 82, 28, 20);
        Put(Field(description, Colors.White, "Descripción"), 118, 110, 300, 20);
        Put(Error, 8, 134, 410, 16);

        parent.TextChanged += (_, _) => UpdateLevel();
        code.Completed += (_, _) => (parent.IsEnabled ? parent : description).Focus();
        parent.Completed += (_, _) => description.Focus();
        description.Completed += async (_, _) => await Accept();
    }

    public void Show(Account? account, List<Account> all)
    {
        editing = account; accounts = all;
        code.Text = account?.Code ?? ""; parent.Text = account?.ParentCode ?? ""; description.Text = account?.Description ?? "";
        code.IsEnabled = parent.IsEnabled = parentSearch.IsEnabled = account == null;
        UpdateLevel();
        Open(account == null ? "Agregando una Cuenta" : "Cambiando una Cuenta", account == null ? code : description);
    }

    private void UpdateLevel()
    {
        var parentCode = parent.Text?.Trim() ?? "";
        var superior = accounts.FirstOrDefault(a => a.Code == parentCode);
        level.Text = parentCode.Length == 0 ? "1" : superior == null ? "?" : (superior.Level + 1).ToString();
        last.Text = editing != null && accounts.Any(a => a.ParentCode == editing.Code) ? "N" : "S";
    }

    private async Task PickParent()
    {
        var typed = parent.Text?.Trim() ?? "";
        var matches = accounts.Where(a => a.Code.StartsWith(typed, StringComparison.OrdinalIgnoreCase)).Take(60)
            .Select(a => $"{a.Code}  {a.Description}").ToArray();
        if (matches.Length == 0) { Error.Text = "No hay cuentas que empiecen con ese número."; return; }
        var choice = await Owner.DisplayActionSheetAsync("Cuenta superior", "Cancelar", null, matches);
        if (choice != null && matches.Contains(choice)) parent.Text = choice[..choice.IndexOf("  ", StringComparison.Ordinal)];
        description.Focus();
    }

    protected override async Task<bool> Save()
    {
        if (editing == null) await db.AddAccount(company.Id, code.Text ?? "", description.Text ?? "", parent.Text ?? "");
        else await db.UpdateAccount(company.Id, editing.Id, description.Text ?? "");
        IsVisible = false;
        await saved(editing?.Code ?? (code.Text ?? "").Trim());
        return true;
    }
}
