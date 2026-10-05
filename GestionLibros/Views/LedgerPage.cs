using GestionLibros.Data;
using GestionLibros.Models;
using GestionLibros.Reporting;

namespace GestionLibros.Views;

public class LedgerPage : AccountingPage
{
    private record AccountOption(Account Account) { public override string ToString() => $"{Account.Code} — {Account.Description}"; }
    private readonly AppDatabase db;
    private readonly Company company;
    private readonly int? initialAccount;
    private readonly Picker account = new() { Title = "Seleccione una cuenta" };
    private readonly DatePicker from = new();
    private readonly DatePicker through = new();
    private readonly CheckBox children = new() { IsChecked = true };
    private readonly VerticalStackLayout rows = new() { Spacing = 1, MinimumWidthRequest = 1200 };
    private readonly Label totals = AccountingUi.Label("Seleccione cuenta y fechas, luego pulse Consultar.", true);
    private bool initialized;
    private string? lastExport;
    private readonly Button openExport;

    public LedgerPage(AppDatabase db, Company company, int year, int month, int? accountId = null)
        : base("Mayor de Cuenta", db, company, year, month)
    {
        this.db = db; this.company = company; initialAccount = accountId;
        from.Date = new DateTime(year, month, 1); through.Date = from.Date.Value.AddMonths(1).AddDays(-1);
        Body.Children.Add(account);
        var dates = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        dates.Children.Add(AccountingUi.Label("Desde")); dates.Children.Add(from);
        dates.Children.Add(AccountingUi.Label("Hasta")); dates.Children.Add(through);
        dates.Children.Add(children); dates.Children.Add(AccountingUi.Label("Incluir subcuentas")); Body.Children.Add(dates);
        openExport = AccountingUi.Button("Abrir CSV guardado", () => Guard(async () =>
        {
            if (lastExport != null && !await ReportActions.Open(lastExport)) Notice.Text = $"Abra el archivo: {lastExport}";
        }));
        var actions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        actions.Children.Add(AccountingUi.Button("Consultar", () => Guard(async () => { await Load(); })));
        actions.Children.Add(AccountingUi.Button("Vista previa", () => Guard(async () => { var doc = await Load(); await Navigation.PushAsync(new ReportPreviewPage(doc, false)); })));
        actions.Children.Add(AccountingUi.Button("Exportar CSV", () => Guard(async () =>
        {
            lastExport = await ReportActions.Save(await Load(), $"Mayor-{company.Id}", false);
            openExport.IsEnabled = true; Notice.Text = $"CSV guardado: {lastExport}. Importes con punto decimal; texto UTF-8.";
        })));
        actions.Children.Add(AccountingUi.Button("Imprimir / PDF", () => Guard(async () =>
        {
            var path = await ReportActions.Save(await Load(), $"Mayor-{company.Id}", true);
            Notice.Text = await ReportActions.Open(path) ? "Use Imprimir / Guardar PDF en el navegador." : $"Abra el reporte con un navegador: {path}";
        })));
        openExport.IsEnabled = false; actions.Children.Add(openExport);
        actions.Children.Add(AccountingUi.Button("Salir", async () => await Navigation.PopAsync()));
        actions.Children.Add(AccountingUi.Button("Ayuda", () => DisplayAlertAsync("Ayuda", "Elija cuenta y fechas, luego use Consultar.", "Cerrar")));
        Body.Children.Add(actions); Body.Children.Add(AccountingUi.Table(rows)); Body.Children.Add(totals);
        Body.Children.Add(AccountingUi.Label("Saldo deudor positivo y acreedor negativo. Para cuentas superiores, Incluir subcuentas permite ver los movimientos de sus descendientes."));
        Body.Children.Add(Notice);
        account.SelectedIndexChanged += (_, _) => Invalidate();
        from.DateSelected += (_, _) => Invalidate(); through.DateSelected += (_, _) => Invalidate();
        children.CheckedChanged += (_, _) => Invalidate();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await Guard(async () =>
        {
            if (!initialized)
            {
                var options = (await db.Accounts(company.Id)).Select(a => new AccountOption(a)).ToList();
                account.ItemsSource = options;
                account.SelectedItem = options.FirstOrDefault(a => a.Account.Id == initialAccount) ?? options.FirstOrDefault();
                initialized = true;
            }
            if (account.SelectedItem != null) await Load();
            else totals.Text = "Esta contabilidad todavía no tiene cuentas.";
        });
    }

    private void Invalidate()
    {
        rows.Children.Clear(); totals.Text = "Filtros modificados. Pulse Consultar para ver el mayor.";
        lastExport = null; openExport.IsEnabled = false;
    }

    private async Task<ReportDocument> Load()
    {
        if (account.SelectedItem is not AccountOption selected) throw new InvalidOperationException("Seleccione una cuenta.");
        if (from.Date == null || through.Date == null) throw new InvalidOperationException("Seleccione las fechas inicial y final.");
        var report = await db.Ledger(company.Id, selected.Account.Id, from.Date.Value, through.Date.Value, children.IsChecked);
        var document = ReportFormatter.Ledger(report);
        rows.Children.Clear(); rows.Children.Add(AccountingUi.Row(document.Headers.ToArray(), true));
        foreach (var row in document.Rows) rows.Children.Add(AccountingUi.Row(row.Cells.Select(c => c.Display).ToArray(), row.Summary, linkColumns: row.Summary ? 0 : 3));
        totals.Text = $"Saldo inicial: {AccountingUi.Money(report.Opening)} · Cargos: {AccountingUi.Money(report.Debits)} · Créditos: {AccountingUi.Money(report.Credits)} · Saldo final: {AccountingUi.Money(report.Closing)}";
        return document;
    }
}
