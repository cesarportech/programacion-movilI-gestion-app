using GestionLibros.Data;
using GestionLibros.Models;
using GestionLibros.Reporting;

namespace GestionLibros.Views;

public class BalancesPage : AccountingPage
{
    private readonly AppDatabase db;
    private readonly Company company;
    private readonly int year, month;
    private readonly VerticalStackLayout rows = new() { Spacing = 1, MinimumWidthRequest = 1050 };
    private readonly Entry search = new() { Placeholder = "Localizador: cuenta o nombre" };
    private List<BalanceRow> balances = [];

    public BalancesPage(AppDatabase db, Company company, int year, int month)
        : base("Consulta de Cuentas", db, company, year, month, "Recorriendo Registros")
    {
        this.db = db; this.company = company; this.year = year; this.month = month;
        Body.Children.Add(search); search.TextChanged += (_, _) => Render();
        Body.Children.Add(AccountingUi.Table(rows));
        Body.Children.Add(AccountingUi.Label("Seleccione una fila para abrir el mayor de esa cuenta. Saldo deudor positivo y acreedor negativo. Las cuentas superiores incluyen sus descendientes."));
        var actions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        actions.Children.Add(AccountingUi.Button("Filtro", () => Guard(async () => { Render(); await Task.CompletedTask; })));
        actions.Children.Add(AccountingUi.Button("Actualizar", () => Guard(Load)));
        actions.Children.Add(AccountingUi.Button("Exportar", () => Guard(async () =>
        {
            var file = await ReportActions.Save(await Document(), $"Saldos-{company.Id}-{year}-{month:00}", false);
            Notice.Text = $"CSV guardado: {file}. Exporta las cuentas del filtro actual.";
        })));
        actions.Children.Add(AccountingUi.Button("Imprimir", () => Guard(async () =>
        {
            await Navigation.PushAsync(new ReportPreviewPage(await Document(), false));
        })));
        actions.Children.Add(AccountingUi.Button("Salir", async () => await Navigation.PopAsync()));
        actions.Children.Add(AccountingUi.Button("Ayuda", () => DisplayAlertAsync("Ayuda", "Seleccione una cuenta para abrir su mayor. Use Imprimir para la vista previa.", "Cerrar")));
        Body.Children.Add(actions); Body.Children.Add(Notice);
    }

    protected override async void OnAppearing() { base.OnAppearing(); await Guard(Load); }
    private async Task Load() { balances = await db.Balances(company.Id, year, month); Render(); }
    private List<BalanceRow> Filtered()
    {
        var term = search.Text ?? "";
        return balances.Where(b => b.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            b.Description.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
    }
    private async Task<ReportDocument> Document()
    {
        await Load();
        return ReportFormatter.Balances(company.Name, year, month, search.Text ?? "", Filtered());
    }
    private void Render()
    {
        rows.Children.Clear(); rows.Children.Add(AccountingUi.Row(["Cuenta", "Descripción", "Saldo inicial", "Cargos", "Créditos", "Saldo actual"], true));
        foreach (var b in Filtered())
            rows.Children.Add(AccountingUi.Row([b.Code, b.Description, AccountingUi.Money(b.Opening),
                AccountingUi.Money(b.Debits), AccountingUi.Money(b.Credits), AccountingUi.Money(b.Closing)],
                linkColumns: 2,
                select: async () => await Guard(async () =>
                {
                    var account = (await db.Accounts(company.Id)).FirstOrDefault(a => a.Code == b.Code)
                        ?? throw new InvalidOperationException("La cuenta ya no está disponible.");
                    await Navigation.PushAsync(new LedgerPage(db, company, year, month, account.Id));
                })));
    }
}
