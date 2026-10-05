using GestionLibros.Data;
using GestionLibros.Models;
using GestionLibros.Reporting;

namespace GestionLibros.Views;

public class TrialBalancePage : AccountingPage
{
    private readonly AppDatabase db;
    private readonly Company company;
    private readonly int year, month;
    private readonly Picker maximumLevel = new() { Title = "Nivel máximo", WidthRequest = 110 };
    private readonly CheckBox includeZero = new() { IsChecked = true };
    private readonly CheckBox monochrome = new();
    private readonly VerticalStackLayout rows = new() { Spacing = 1, MinimumWidthRequest = 1300 };
    private readonly Label totals = AccountingUi.Label("", true);
    private string? lastExport;
    private readonly Button openExport;

    public TrialBalancePage(AppDatabase db, Company company, int year, int month)
        : base("Reporte Balanza de Comprobación", db, company, year, month)
    {
        this.db = db; this.company = company; this.year = year; this.month = month;
        maximumLevel.ItemsSource = Enumerable.Range(1, 9).Select(i => i.ToString()).ToList();
        maximumLevel.SelectedIndex = 8;
        var options = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        options.Children.Add(AccountingUi.Label("Nivel máximo")); options.Children.Add(maximumLevel);
        options.Children.Add(includeZero); options.Children.Add(AccountingUi.Label("Incluir cuentas sin actividad"));
        options.Children.Add(monochrome); options.Children.Add(AccountingUi.Label("Impresión B y N"));
        Body.Children.Add(options);
        openExport = AccountingUi.Button("Abrir CSV guardado", () => Guard(async () =>
        {
            if (lastExport != null && !await ReportActions.Open(lastExport)) Notice.Text = $"Abra el archivo: {lastExport}";
        }));
        var actions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        actions.Children.Add(AccountingUi.Button("Consultar", () => Guard(async () => { await Load(); })));
        actions.Children.Add(AccountingUi.Button("Vista previa", () => Guard(async () => { var document = await Load(); await Navigation.PushAsync(new ReportPreviewPage(document, monochrome.IsChecked)); })));
        actions.Children.Add(AccountingUi.Button("Exportar CSV", () => Guard(async () =>
        {
            lastExport = await ReportActions.Save(await Load(), $"Balanza-{company.Id}-{year}-{month:00}", false);
            openExport.IsEnabled = true;
            Notice.Text = $"CSV guardado: {lastExport}. Importes con punto decimal; texto UTF-8.";
        })));
        actions.Children.Add(AccountingUi.Button("Imprimir / PDF", () => Guard(async () =>
        {
            var path = await ReportActions.Save(await Load(), $"Balanza-{company.Id}-{year}-{month:00}", true, monochrome.IsChecked);
            Notice.Text = await ReportActions.Open(path) ? "Use Imprimir / Guardar PDF en el navegador." : $"Abra el reporte con un navegador: {path}";
        })));
        openExport.IsEnabled = false;
        actions.Children.Add(openExport);
        actions.Children.Add(AccountingUi.Button("Cancelar", async () => await Navigation.PopAsync()));
        actions.Children.Add(AccountingUi.Button("Ayuda", () => DisplayAlertAsync("Ayuda", "Elija el nivel máximo y use Consultar. Imprimir / PDF abre el reporte en el navegador.", "Cerrar")));
        Body.Children.Add(actions); Body.Children.Add(AccountingUi.Table(rows)); Body.Children.Add(totals);
        Body.Children.Add(AccountingUi.Label("Los totales generales incluyen cuentas de detalle de todos los niveles. Las filas visibles pueden contener cuentas superiores y sus subcuentas; no deben sumarse entre sí."));
        Body.Children.Add(Notice);
        maximumLevel.SelectedIndexChanged += async (_, _) => await Guard(async () => { await Load(); });
        includeZero.CheckedChanged += async (_, _) => await Guard(async () => { await Load(); });
    }

    protected override async void OnAppearing() { base.OnAppearing(); await Guard(async () => { await Load(); }); }

    private async Task<ReportDocument> Load()
    {
        var report = await db.TrialBalance(company.Id, year, month, maximumLevel.SelectedIndex + 1, includeZero.IsChecked);
        var document = ReportFormatter.TrialBalance(report);
        rows.Children.Clear(); rows.Children.Add(AccountingUi.Row(document.Headers.ToArray(), true));
        foreach (var row in document.Rows) rows.Children.Add(AccountingUi.Row(row.Cells.Select(c => c.Display).ToArray(), row.Summary, linkColumns: row.Summary ? 0 : 2));
        totals.Text = report.Totals.IsBalanced ? "Sumas iguales · Balanza cuadrada" : "La balanza presenta diferencias";
        totals.TextColor = report.Totals.IsBalanced ? Colors.DarkBlue : Colors.DarkRed;
        return document;
    }
}
