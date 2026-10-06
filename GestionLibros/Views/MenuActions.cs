
using System.Text;
using GestionLibros.Data;
using GestionLibros.Data.Import;
using GestionLibros.Models;

namespace GestionLibros.Views;

// Menu and toolbar actions that do not open a module screen. Every page routes its keys here first,
// so the main screen and the modules behave the same.
internal static class MenuActions
{
    // GL options that have no meaning in FREDI, with the reason shown instead of "no disponible".
    private static readonly Dictionary<string, string> NotApplicable = new()
    {
        ["Estilos"] = "FREDI usa un estilo fijo igual al GL2000; no hay archivo de estilos que configurar.",
        ["Editar Archivo INI"] = "FREDI no usa archivos INI: la configuración vive en su base local.",
        ["Generar Archivo INI"] = "FREDI no usa archivos INI: la configuración vive en su base local.",
        ["Configurar Impresora ..."] = "La impresora se elige en el cuadro de impresión que abre Imprimir (salida Impresora).",
        ["Ventana · Cascada"] = "FREDI muestra una pantalla a la vez; las ventanas de reporte se abren encima y se pueden mover.",
        ["Mantenimiento de Archivos"] = "La base de FREDI no requiere reindexar ni compactar archivos como el GL2000. Use Sistema de Respaldos para copias.",
    };

    internal static string? NotApplicableReason(string name) => NotApplicable.GetValueOrDefault(name);

    internal const string AccumulateText = "En FREDI los saldos se acumulan solos: Consulta, Balanza, Saldos, Mayor y los estados financieros " +
        "se calculan con todas las pólizas guardadas al momento de abrirlos. No hace falta Acumular Saldos.";

    internal static Task Accumulate(Page page) => page.DisplayAlertAsync("Acumular Saldos", AccumulateText, "Entendido");

    // Returns true when the key was handled here.
    internal static async Task<bool> TryRun(Page page, AppDatabase db, Company company, int year, int month, string key)
    {
        switch (key)
        {
            case "import-catalog": await CatalogImport.Run(page, db, company, year, month); return true;
            case "logout": await SessionActions.SignOut(page, db); return true;
            case "accumulate": await Accumulate(page); return true;
            case "policy-types": ReportDialog.Show(page, new PolicyTypesDialog(page, db, company)); return true;
            case "backup": await Backup(page, db); return true;
            case "catalog-csv": await CatalogCsvFile(page, db, company); return true;
            case "sessions": await Sessions(page, db); return true;
        }
        if (ReportDialog.For(key, page, db, company, year, month) is { } report) { ReportDialog.Show(page, report); return true; }
        return false;
    }

    private static async Task Backup(Page page, AppDatabase db)
    {
        var folder = Path.Combine(FileSystem.AppDataDirectory, "Respaldos");
        if (!await page.DisplayAlertAsync("Sistema de Respaldos", $"Se hará una copia completa de la base de FREDI en:\n{folder}", "Respaldar", "Cancelar")) return;
        try
        {
            var file = await db.Backup(folder);
            await page.DisplayAlertAsync("Sistema de Respaldos", $"Respaldo creado:\n{file}\n\nPara recuperar, cierre FREDI y reemplace fredi-pruebas-v1.db3 por esta copia.", "Aceptar");
        }
        catch (InvalidOperationException ex) { await page.DisplayAlertAsync("Sistema de Respaldos", ex.Message, "Cerrar"); }
    }

    private static async Task Sessions(Page page, AppDatabase db)
    {
        try
        {
            var sessions = await db.ActiveSessions();
            var text = sessions.Count == 0 ? "No hay sesiones abiertas." : string.Join("\n", sessions.Select(s =>
                $"{s.Username}{(s.IsMaster ? " (maestro)" : "")} · desde {s.CreatedAt:d/M/yyyy HH:mm} · último uso {s.LastUsedAt:d/M/yyyy HH:mm}"));
            await page.DisplayAlertAsync("Usuarios Conectados", text + "\n\nUna sesión sigue abierta hasta que el usuario elige Cerrar Sesión.", "Cerrar");
        }
        catch (InvalidOperationException ex) { await page.DisplayAlertAsync("Usuarios Conectados", ex.Message, "Cerrar"); }
    }

    // Importar / Exportar Catálogo CSV: Cuenta, Cuenta Superior, Descripción.
    private static async Task CatalogCsvFile(Page page, AppDatabase db, Company company)
    {
        var choice = await page.DisplayActionSheetAsync("Catálogo CSV", "Cancelar", null, "Exportar catálogo a CSV", "Importar catálogo desde CSV");
        try
        {
            if (choice == "Exportar catálogo a CSV")
            {
                var folder = Path.Combine(FileSystem.AppDataDirectory, "Reportes");
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, $"Catalogo-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
                await File.WriteAllTextAsync(file, CatalogCsv.Write(await db.Accounts(company.Id)), new UTF8Encoding(true));
                await page.DisplayAlertAsync("Catálogo CSV", $"Catálogo exportado:\n{file}", "Aceptar");
            }
            else if (choice == "Importar catálogo desde CSV")
            {
                var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Catálogo CSV (Cuenta, Cuenta Superior, Descripción)" });
                if (picked == null) return;
                var accounts = CatalogCsv.Parse(await File.ReadAllTextAsync(picked.FullPath));
                if (!await page.DisplayAlertAsync("Catálogo CSV", $"Se agregarán las cuentas nuevas de {accounts.Count} renglones a \"{company.Name}\". Las existentes no cambian.", "Importar", "Cancelar")) return;
                var result = await db.ImportAccounts(company.Id, accounts);
                await page.DisplayAlertAsync("Catálogo CSV", $"Cuentas agregadas: {result.Added}\nYa existían: {result.Skipped}", "Aceptar");
                if (page is CatalogPage catalog) await catalog.Reload();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException)
        { await page.DisplayAlertAsync("Catálogo CSV", ex.Message, "Cerrar"); }
    }

}
