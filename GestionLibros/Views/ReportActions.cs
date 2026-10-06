using System.Text;
using GestionLibros.Reporting;

namespace GestionLibros.Views;

internal static class ReportActions
{
    internal static async Task<string> Save(ReportDocument document, string prefix, bool html, bool monochrome = false, bool autoPrint = false)
    {
        var folder = Path.Combine(FileSystem.AppDataDirectory, "Reportes");
        Directory.CreateDirectory(folder);
        var safe = string.Concat(prefix.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        if (safe.Length == 0) safe = "reporte";
        // Unique names never overwrite an earlier export.
        var file = Path.Combine(folder, $"{safe}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.{(html ? "html" : "csv")}");
        // Saved pages are only the GL sheet (no extra buttons), identical to the preview and the print.
        await File.WriteAllTextAsync(file, html ? ReportFormatter.Html(document, monochrome, false, autoPrint) : ReportFormatter.Csv(document), new UTF8Encoding(true));
        return file;
    }

    internal static async Task<bool> Open(string path) => await Launcher.Default.OpenAsync(
        new OpenFileRequest(Path.GetFileName(path), new ReadOnlyFile(path)));

    // Salida Impresora: opens the GL-format report in the browser with its print dialog already shown.
    internal static async Task<string> Print(ReportDocument document, bool monochrome)
    {
        var path = await Save(document, document.Title, true, monochrome, autoPrint: true);
        if (!await Open(path)) throw new InvalidOperationException($"No se pudo abrir el reporte para imprimir. Archivo: {path}");
        return path;
    }
}

// Salida Pantalla: "Vista Previa" of the printed page inside FREDI.
public class ReportPreviewPage : ContentPage
{
    public ReportPreviewPage(ReportDocument document, bool monochrome)
    {
        Title = "Vista Previa... " + document.Title;
        NavigationPage.SetHasNavigationBar(this, false);
        BackgroundColor = Color.FromArgb("#808080");
        var status = ClassicWorkspaceChrome.Text("");
        var tools = new HorizontalStackLayout { Spacing = 5, Padding = new Thickness(6, 3) };
        tools.Children.Add(JournalsPage.ActionButton("Imprimir", "gl_policy.png", async () =>
        {
            try { status.Text = "Enviado al navegador para imprimir: " + await ReportActions.Print(document, monochrome); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); status.Text = ex is InvalidOperationException ? ex.Message : "No se pudo abrir el reporte para imprimir."; }
        }, 90));
        tools.Children.Add(JournalsPage.ActionButton("Exportar", "gl_query.png", async () =>
        {
            try { status.Text = "CSV guardado: " + await ReportActions.Save(document, document.Title, false); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); status.Text = "No se pudo guardar el CSV."; }
        }, 90));
        tools.Children.Add(JournalsPage.ActionButton("Cerrar", "gl_close.png", async () => await Navigation.PopAsync(), 82));
        status.Margin = new Thickness(12, 0); tools.Children.Add(status);
        // The preview opens the same HTML file that is printed; loading it as a string made the viewer
        // fall back to a compatibility mode with larger table text.
        var preview = Path.Combine(FileSystem.CacheDirectory, $"vista-previa-{Guid.NewGuid():N}.html");
        File.WriteAllText(preview, ReportFormatter.Html(document, monochrome, false), new UTF8Encoding(true));
        var browser = new WebView { Source = new UrlWebViewSource { Url = new Uri(preview).AbsoluteUri } };
        Unloaded += (_, _) => { try { File.Delete(preview); } catch (IOException) { } };
        var layout = new Grid { RowSpacing = 0, RowDefinitions = { new RowDefinition(34), new RowDefinition(GridLength.Star) } };
        layout.Add(new Border { Stroke = Colors.Gray, StrokeThickness = 1, Padding = 0, Background = ClassicWorkspaceChrome.ToolbarBrush(), Content = tools }, 0, 0);
        layout.Add(browser, 0, 1);
        Content = layout;
        Loaded += (_, _) => { if (Window != null) ClassicWorkspaceChrome.WindowTitle(Window, Title); };
    }
}
