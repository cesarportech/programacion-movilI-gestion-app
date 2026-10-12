using GestionLibros.Data;
using GestionLibros.Models;
using System.Globalization;
using System.Text.Json;
namespace GestionLibros.Views;
internal static class MigrationActions
{
 internal static async Task Run(Page page, AppDatabase db, Company company)
 {
  try
  {
   var policy = await db.MigrationCutoff(company.Id);
   var state = policy == null ? "Sin corte configurado" : $"GL2000 hasta {policy.LegacyThrough:dd/MM/yyyy}; FREDI desde {policy.LegacyThrough.AddDays(1):dd/MM/yyyy}";
   var choice = await page.DisplayActionSheetAsync(state, "Cerrar", null, "Definir corte", "Ver lotes y procedencia", "Importar detalle mensual (JSON)");
   if (choice == "Definir corte")
   {
    var text = await page.DisplayPromptAsync("Corte GL2000", "Último día del mes cubierto por GL2000 (aaaa-MM-dd). Las operaciones nuevas comienzan al día siguiente. No podrá mover el corte después de importar lotes.", "Revisar", "Cancelar", placeholder: "2026-09-30", initialValue: policy?.LegacyThrough.ToString("yyyy-MM-dd") ?? "");
    if (text == null) return;
    if (!DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var through)) throw new InvalidOperationException("Use el formato aaaa-MM-dd.");
    if (!await page.DisplayAlertAsync("Confirmar corte", $"{company.Name}\nGL2000 hasta {through:dd/MM/yyyy}\nFREDI desde {through.AddDays(1):dd/MM/yyyy}\nLas partidas existentes no se borrarán ni ocultarán. Se rechazará cualquier solapamiento.", "Establecer", "Cancelar")) return;
    await db.SetMigrationCutoff(company.Id, through);
    await page.DisplayAlertAsync("Corte establecido", "Las siguientes importaciones y capturas respetarán este límite.", "Aceptar");
   }
   else if (choice == "Ver lotes y procedencia")
   {
    var batches = await db.MigrationBatches(company.Id);
    if (batches.Count == 0) { await page.DisplayAlertAsync("Lotes", "No hay lotes registrados.", "Cerrar"); return; }
    var options = batches.Select(b => $"{b.Year} · {b.FromMonth:00}–{b.ThroughMonth:00} · {b.Kind} · {b.Id}").ToArray();
    var selected = await page.DisplayActionSheetAsync("Lotes de importación", "Cerrar", null, options);
    var index = Array.IndexOf(options, selected); if (index < 0) return;
    var b = batches[index];
    if (await page.DisplayAlertAsync("Procedencia del lote", $"{b.Id}\n{b.Kind}\nOrigen: {b.Source}\nEjercicio: {b.Year}; períodos: {b.FromMonth} a {b.ThroughMonth} (0 = apertura)\nImportado por {b.ImportedBy}, {b.ImportedUtc.ToLocalTime():dd/MM/yyyy HH:mm}\nHuella del contenido normalizado: {b.Fingerprint}\nSustituido por: {(string.IsNullOrEmpty(b.SupersededBy) ? "No" : b.SupersededBy)}\nAcumulados sustituidos por este detalle (solo su mes): {b.ReplacesBatchIds}\nSe conserva la evidencia del lote. Otros meses y la apertura permanecen como acumulados.", "Exportar evidencia", "Cerrar"))
    {
     var path = Path.Combine(FileSystem.AppDataDirectory, $"lote-{b.Id}.json");
     await File.WriteAllTextAsync(path, JsonSerializer.Serialize(b, new JsonSerializerOptions { WriteIndented = true }));
     await page.DisplayAlertAsync("Evidencia guardada", path, "Cerrar");
    }
   }
   else if (choice == "Importar detalle mensual (JSON)")
   {
    if (policy == null) throw new InvalidOperationException("Defina primero el corte de migración.");
    var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Detalle GL2000 normalizado (JSON, un mes completo)" });
    if (file == null) return;
    using var stream = await file.OpenReadAsync();
    var content = await JsonSerializer.DeserializeAsync<MigrationDetailFile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (content == null) throw new InvalidOperationException("Archivo vacío.");
    if (!await page.DisplayAlertAsync("Conciliar y sustituir acumulado", $"{company.Name}\nEjercicio {content.Year}, mes {content.Month}; {content.Journals?.Count ?? 0} pólizas.\nSolo se sustituirá el acumulado si coinciden los cargos y créditos de todas las cuentas. Un lote repetido no se añadirá otra vez.", "Conciliar e importar", "Cancelar")) return;
    var batch = await db.ImportHistoricalDetails(company.Id, content, file.FileName);
    await page.DisplayAlertAsync("Detalle disponible", $"Lote: {batch}\nEl mes se calcula con su detalle y deja de sumar su acumulado. Si el lote ya existía, se conservó sin duplicarlo.", "Aceptar");
   }
  }
  catch (Exception ex) when (ex is InvalidOperationException or JsonException or IOException or OverflowException)
  { await page.DisplayAlertAsync("Migración", ex.Message, "Cerrar"); }
 }
}
