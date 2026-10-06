using GestionLibros.Data;
using GestionLibros.Data.Import;
using GestionLibros.Models;

namespace GestionLibros.Views;

// Utilerías → Importar Catálogo GL2000: copies a catalog from the old system into the active company.
// The GL2000 files are only read; nothing is written to C:\Sistemas.
internal static class CatalogImport
{
    private const string OtherFile = "Buscar otro archivo .Tps…";

    // Shows errors itself so it is safe to call from any menu, with or without a page-level Guard.
    internal static async Task Run(Page page, AppDatabase db, Company company, int year, int month)
    {
        try { await Import(page, db, company, year, month); }
        catch (InvalidOperationException ex) { await page.DisplayAlertAsync("Importar catálogo", ex.Message, "Cerrar"); }
    }

    private static async Task Import(Page page, AppDatabase db, Company company, int year, int month)
    {
        var catalogs = await Task.Run(() => GlCatalog.Find());
        var options = catalogs.Select(c => c.Label).Append(OtherFile).ToArray();
        var choice = await page.DisplayActionSheetAsync($"Importar catálogo GL2000 en {company.Name}", "Cancelar", null, options);
        if (choice == null || choice == "Cancelar") return;

        string path; string label;
        if (choice == OtherFile)
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Catálogo del GL2000 (CAT*.Tps)" });
            if (file == null) return;
            path = file.FullPath; label = Path.GetFileName(path);
        }
        else
        {
            var selected = catalogs[Array.IndexOf(options, choice)];
            path = selected.Path; label = selected.Label;
        }

        List<GlAccount> accounts;
        try { accounts = await Task.Run(() => GlCatalog.Read(path)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        { throw new InvalidOperationException("No se pudo leer el catálogo: " + ex.Message); }

        var fiscalYear = GlCatalog.YearOf(path);
        var balances = fiscalYear == null
            ? "Este archivo no tiene ejercicio (plantilla): solo se copian las cuentas."
            : $"También se copian los acumulados de {fiscalYear} (saldo inicial y cargos/créditos de cada mes) y reemplazan los importados antes para ese año.";
        if (!await page.DisplayAlertAsync("Importar catálogo",
            $"Se copiarán {accounts.Count} cuentas de {label} a \"{company.Name}\".\n\n" +
            $"Las cuentas que ya existen en FREDI no se modifican. {balances} " +
            "Los archivos del GL2000 solo se leen.", "Importar", "Cancelar")) return;

        var result = await db.ImportAccounts(company.Id, accounts, fiscalYear);
        await page.DisplayAlertAsync("Catálogo importado",
            $"Cuentas agregadas: {result.Added}\nYa existían (sin cambios): {result.Skipped}" +
            (fiscalYear == null ? "" : $"\nCuentas con acumulados de {fiscalYear}: {result.BalanceAccounts}"), "Aceptar");
        if (page is CatalogPage catalog) await catalog.Reload();
        else await page.Navigation.PushAsync(new CatalogPage(db, company, year, month));
    }
}
