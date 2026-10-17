using System.Text;
using GestionLibros.Data.Import;
using GestionLibros.Models;
using GestionLibros.Reporting;

namespace GestionLibros.Views;

// Póliza screen: Imprimir and Excel (Reporte de Asientos Contables) and, while the partida is empty,
// Importar from the FREDI template (.xlsx or .csv).
public partial class JournalEditor
{
    private View? importButton;

    private View TransferButtons()
    {
        var row = new HorizontalStackLayout { Spacing = 5 };
        row.Children.Add(JournalsPage.ActionButton("Imprimir", "gl_policy.png", () => Guard(PrintEntry), 90));
        row.Children.Add(JournalsPage.ActionButton("Excel", "gl_query.png", () => Guard(ExportEntry), 80));
        importButton = JournalsPage.ActionButton("Importar", "gl_add.png", () => Guard(ImportTemplate), 92);
        row.Children.Add(importButton);
        UpdateImportButton();
        return row;
    }

    // Importar is only offered while the partida has no movements.
    private void UpdateImportButton()
    {
        if (importButton != null) importButton.IsVisible = !viewOnly && lines.Count == 0;
    }

    // What is on screen, saved or not, in the printable shape.
    private PolicyPrint ScreenEntry()
    {
        if (lines.Count == 0) throw new InvalidOperationException("La partida no tiene movimientos.");
        var byId = accounts.ToDictionary(a => a.Id);
        var journal = new Journal { Type = (type.Text ?? "").Trim(), Reference = (reference.Text ?? "").Trim(),
            Date = date.Date ?? original.Date, Concept = (concept.Text ?? "").Trim() };
        var printed = lines.Select(l => new PolicyPrintLine(byId.TryGetValue(l.AccountId, out var a) ? a.Code : "?", a?.Description ?? "?",
            l.Concept.Length > 0 ? l.Concept : journal.Concept, l.DebitCents, l.CreditCents)).ToList();
        return new PolicyPrint(journal, typeName.Text ?? "", printed);
    }

    private async Task PrintEntry() =>
        await Navigation.PushAsync(new ReportPreviewPage(ReportFormatter.JournalEntries(company.Name, [ScreenEntry()]), false));

    private async Task ExportEntry()
    {
        var entry = ScreenEntry();
        var file = NewFile($"Partida-{entry.Journal.Type}-{entry.Journal.Reference}", "xlsx");
        await using (var stream = File.Create(file)) Xlsx.Write(stream, ReportFormatter.JournalEntriesSheet(company.Name, [entry]));
        Notice.Text = "Excel guardado: " + file;
        if (!await ReportActions.Open(file)) await DisplayAlertAsync("Excel", $"Partida exportada en:\n{file}", "Aceptar");
    }

    private async Task ImportTemplate()
    {
        if (viewOnly) return;
        if (lines.Count > 0) throw new InvalidOperationException("Importar solo está disponible cuando la partida está vacía.");
        const string import = "Importar plantilla (.xlsx o .csv)…", blank = "Descargar plantilla vacía";
        var choice = await DisplayActionSheetAsync("Importar partida", "Cancelar", null, import, blank);
        if (choice == blank) { await SaveBlankTemplate(); return; }
        if (choice != import) return;

        var picked = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Plantilla de partida (.xlsx o .csv)" });
        if (picked == null) return;
        List<string[]> rows;
        try { rows = ReadRows(picked.FullPath); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException)
        { throw new InvalidOperationException("No se pudo leer el archivo: " + ex.Message); }

        TemplateJournal template;
        try { template = JournalTemplate.Parse(rows); }
        catch (FormatException ex) { await DisplayAlertAsync("La plantilla tiene errores", ex.Message + "\n\nNo se importó nada.", "Cerrar"); return; }

        // Accounts must exist in this accounting and be detail accounts (no subaccounts).
        // Codes Excel turned into numbers (1.12100 → 1.121) are matched to the catalog by value.
        var parents = accounts.Select(a => a.ParentCode).ToHashSet();
        var byCode = accounts.ToDictionary(a => a.Code);
        var errors = new List<string>();
        var resolved = new Dictionary<TemplateLine, Account>();
        foreach (var line in template.Lines)
        {
            var code = JournalTemplate.Resolve(line.AccountCode, byCode.Keys, out var ambiguous);
            if (ambiguous) errors.Add($"Renglón {line.Row}: la cuenta {line.AccountCode} coincide con varias cuentas; ponga la columna Cuenta en formato Texto y escriba el código completo.");
            else if (code == null) errors.Add($"Renglón {line.Row}: la cuenta {line.AccountCode} no existe en {company.Name}.");
            else if (parents.Contains(code)) errors.Add($"Renglón {line.Row}: la cuenta {code} tiene subcuentas; use una cuenta de detalle.");
            else resolved[line] = byCode[code];
        }
        if (errors.Count > 0) { await DisplayAlertAsync("La plantilla tiene errores", string.Join("\n", errors.Take(12)) + (errors.Count > 12 ? $"\n… y {errors.Count - 12} más." : "") + "\n\nNo se importó nada.", "Cerrar"); return; }
        // Tipo typed as 01 may arrive as 1: use the catalog's spelling when it matches by value.
        var typeCode = JournalTemplate.Resolve(template.Type, policyTypes.Select(t => t.Code), out _) ?? template.Type;
        template = template with { Type = typeCode };

        if (!await DisplayAlertAsync("Importar partida",
            $"Póliza {template.Type}-{template.Reference} del {template.Date:d/M/yyyy}\n{template.Lines.Count} movimientos · Cargos {template.Debits / 100m:N2} · Créditos {template.Credits / 100m:N2}\n\n" +
            "Se cargará en esta pantalla para revisarla; se guarda al pulsar Aceptar.", "Importar", "Cancelar")) return;

        type.Text = template.Type; reference.Text = template.Reference; date.Date = template.Date; concept.Text = template.Concept;
        lines.Clear();
        lines.AddRange(template.Lines.Select(l => new JournalLine { AccountId = resolved[l].Id, Concept = l.Concept,
            DebitCents = l.DebitCents, CreditCents = l.CreditCents }));
        selected = lines.FirstOrDefault();
        Render();
        Notice.Text = $"Partida importada ({lines.Count} movimientos). Revise y pulse Aceptar para guardarla.";
    }

    private async Task SaveBlankTemplate()
    {
        var file = NewFile("Plantilla-Partida", "xlsx");
        await using (var stream = File.Create(file))
            Xlsx.Write(stream,
                // Tipo, Referencia and Cuenta are Text columns so Excel keeps 01, leading zeros and 1.12100 as typed.
                new Xlsx.Sheet("Partida", [JournalTemplate.Headers], HeaderRow: 0, Widths: [8, 12, 12, 32, 12, 32, 14, 14], TextColumns: [0, 1, 4]),
                new Xlsx.Sheet("Instrucciones", JournalTemplate.Instructions.Select(t => (IReadOnlyList<object?>)new object?[] { t }).ToList(), HeaderRow: 0, Widths: [120]));
        Notice.Text = "Plantilla guardada: " + file;
        if (!await ReportActions.Open(file)) await DisplayAlertAsync("Plantilla", $"Plantilla guardada en:\n{file}", "Aceptar");
    }

    // .xlsx through the built-in reader; .csv as UTF-8, or Windows-1252/Latin-1 when Excel saved it as ANSI.
    private static List<string[]> ReadRows(string path)
    {
        if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(path);
            return Xlsx.Read(stream);
        }
        var bytes = File.ReadAllBytes(path);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes); }
        return JournalTemplate.ReadCsv(text.TrimStart('﻿'));
    }

    private static string NewFile(string prefix, string extension)
    {
        var folder = Path.Combine(FileSystem.AppDataDirectory, "Reportes");
        Directory.CreateDirectory(folder);
        var safe = string.Concat(prefix.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(folder, $"{safe}-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}");
    }
}
