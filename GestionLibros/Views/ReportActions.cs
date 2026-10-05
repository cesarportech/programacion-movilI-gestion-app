using System.Text;
using GestionLibros.Reporting;

namespace GestionLibros.Views;

internal static class ReportActions
{
    internal static async Task<string> Save(ReportDocument document, string prefix, bool html, bool monochrome = false)
    {
        var folder = Path.Combine(FileSystem.AppDataDirectory, "Reportes");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.{(html ? "html" : "csv")}");
        await File.WriteAllTextAsync(file, html ? ReportFormatter.Html(document, monochrome) : ReportFormatter.Csv(document), new UTF8Encoding(true));
        return file;
    }

    internal static async Task<bool> Open(string path) => await Launcher.Default.OpenAsync(
        new OpenFileRequest(Path.GetFileName(path), new ReadOnlyFile(path)));
}

public class ReportPreviewPage : ContentPage
{
    public ReportPreviewPage(ReportDocument document, bool monochrome)
    {
        Title = document.Title;
        var status = new Label { Text = "Vista previa. Para imprimir o guardar PDF, abra el reporte en el navegador.", TextColor = Colors.Black };
        var open = AccountingUi.Button("Abrir para imprimir / PDF", async () =>
        {
            try
            {
                var path = await ReportActions.Save(document, "reporte", true, monochrome);
                status.Text = await ReportActions.Open(path) ? $"Reporte abierto. Archivo: {path}" : $"Guardado en: {path}. Abra el HTML con su navegador.";
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); status.Text = "No se pudo abrir el reporte. Verifique la aplicación predeterminada para HTML."; }
        });
        var browser = new WebView { Source = new HtmlWebViewSource { Html = ReportFormatter.Html(document, monochrome, false) } };
        var panel = new Grid { Padding = 12, RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, RowSpacing = 8 };
        panel.Add(open, 0, 0); panel.Add(status, 0, 1); panel.Add(browser, 0, 2);
        BackgroundColor = Colors.White;
        Content = panel;
    }
}
