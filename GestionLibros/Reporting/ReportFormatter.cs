using System.Globalization;
using System.Net;
using System.Text;
using GestionLibros.Models;

namespace GestionLibros.Reporting;

public record ReportCell(string Value, bool Numeric = false)
{
    public static ReportCell Text(string text) => new(text);
    public static ReportCell Money(long cents) => new((cents / 100m).ToString("F2", CultureInfo.InvariantCulture), true);
    public string Display => Numeric && decimal.TryParse(Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
        ? amount.ToString("N2", CultureInfo.CurrentCulture) : Value;
}
public record ReportRow(IReadOnlyList<ReportCell> Cells, bool Summary = false);
public record ReportDocument(string Title, string Company, string Period, string Options,
    IReadOnlyList<string> Headers, IReadOnlyList<ReportRow> Rows, string Note);

public static class ReportFormatter
{
    private static ReportCell T(string value) => ReportCell.Text(value);
    private static ReportCell M(long value) => ReportCell.Money(value);

    public static ReportDocument TrialBalance(TrialBalanceReport report)
    {
        var rows = report.Rows.Select(b => new ReportRow(new[] { T(b.Code), T(b.Description),
            M(Math.Max(0, b.Opening)), M(Math.Max(0, -b.Opening)), M(b.Debits), M(b.Credits),
            M(Math.Max(0, b.Closing)), M(Math.Max(0, -b.Closing)) })).ToList();
        var totals = report.Totals;
        rows.Add(new(new[] { T("TOTAL GENERAL"), T("Cuentas de detalle"), M(totals.OpeningDebit),
            M(totals.OpeningCredit), M(totals.Debits), M(totals.Credits), M(totals.ClosingDebit), M(totals.ClosingCredit) }, true));
        return new("Balanza de comprobación", report.CompanyName, $"{report.Month:00}/{report.Year}",
            $"Nivel máximo: {report.MaximumLevel}. Cuentas sin actividad: {(report.IncludeZeroAccounts ? "incluidas" : "ocultas")}",
            new[] { "Cuenta", "Descripción", "Inicial deudor", "Inicial acreedor", "Cargos", "Créditos", "Final deudor", "Final acreedor" }, rows,
            "Los totales generales suman cuentas de detalle de toda la contabilidad, independientemente del nivel visible. " +
            "No sume las filas superiores con sus subcuentas. Solo incluye movimientos registrados en FREDI; sin saldos importados. " +
            (totals.IsBalanced ? "Cargos y créditos iguales." : "Hay diferencias: revise los movimientos."));
    }

    public static ReportDocument Ledger(LedgerReport report)
    {
        var rows = new List<ReportRow> { new(new[] { T(""), T(""), T(report.AccountCode), T("Saldo inicial"), M(0), M(0), M(report.Opening) }, true) };
        rows.AddRange(report.Rows.Select(r => new ReportRow(new[] { T(r.Date.ToString("dd/MM/yyyy")),
            T($"{r.Type}-{r.Reference}"), T(r.AccountCode), T(r.Concept), M(r.Debits), M(r.Credits), M(r.Balance) })));
        rows.Add(new(new[] { T("TOTAL"), T(""), T(""), T("Movimientos del período / saldo final"), M(report.Debits), M(report.Credits), M(report.Closing) }, true));
        return new("Mayor de cuenta", report.CompanyName, $"{report.From:dd/MM/yyyy} al {report.Through:dd/MM/yyyy}",
            $"Cuenta: {report.AccountCode} - {report.AccountName}. Subcuentas: {(report.IncludesChildren ? "incluidas" : "excluidas")}",
            new[] { "Fecha", "Póliza", "Cuenta", "Concepto", "Cargos", "Créditos", "Saldo" }, rows,
            "Saldo deudor positivo y acreedor negativo. El saldo inicial incluye movimientos anteriores al rango. " +
            "Orden: fecha, tipo, referencia, identificador de póliza y movimiento. Solo incluye datos registrados en FREDI.");
    }

    public static ReportDocument Balances(string company, int year, int month, string filter, IReadOnlyList<BalanceRow> balances)
    {
        return new("Consulta de cuentas", company, $"{month:00}/{year}",
            string.IsNullOrWhiteSpace(filter) ? "Todas las cuentas" : $"Filtro: {filter}",
            new[] { "Cuenta", "Descripción", "Saldo inicial", "Cargos", "Créditos", "Saldo actual" },
            balances.Select(b => new ReportRow(new[] { T(b.Code), T(b.Description), M(b.Opening), M(b.Debits), M(b.Credits), M(b.Closing) })).ToList(),
            "Saldo deudor positivo y acreedor negativo. Las cuentas superiores incluyen sus descendientes; no sume las filas entre sí. Sin saldos importados.");
    }

    // All text is quoted. Formula-looking text is explicitly exported as text.
    private static string CsvCell(ReportCell cell)
    {
        var value = cell.Value;
        var validNumber = cell.Numeric && decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out _);
        if (!validNumber)
        {
            var trimmed = value.TrimStart();
            if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) value = "'" + value;
        }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static string Csv(ReportDocument document)
    {
        var output = new StringBuilder();
        void Add(IEnumerable<ReportCell> cells) => output.Append(string.Join(",", cells.Select(CsvCell))).Append("\r\n");
        Add(new[] { T("Reporte"), T(document.Title) });
        Add(new[] { T("Contabilidad"), T(document.Company) });
        Add(new[] { T("Período"), T(document.Period) });
        Add(new[] { T("Opciones"), T(document.Options) });
        output.Append("\r\n");
        Add(document.Headers.Select(T));
        foreach (var row in document.Rows) Add(row.Cells);
        output.Append("\r\n");
        Add(new[] { T("Nota"), T(document.Note) });
        return output.ToString();
    }

    public static string Html(ReportDocument document, bool monochrome = false, bool printButton = true)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);
        var output = new StringBuilder("<!doctype html><html lang='es'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
        output.Append("<title>").Append(E(document.Title)).Append("</title><style>");
        output.Append("body{font:14px Arial,sans-serif;color:#17212d;background:white;margin:28px}h1{font-size:23px;margin:0 0 8px}p{margin:6px 0;overflow-wrap:anywhere}table{width:100%;border-collapse:collapse;margin-top:20px}th,td{border:1px solid #b8c0ca;padding:7px;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{background:");
        output.Append(monochrome ? "#eee" : "#dce8f6");
        output.Append("}td.number{text-align:right;white-space:nowrap}tr.summary{font-weight:bold;background:#f2f2f2}thead{display:table-header-group}tr{break-inside:avoid}.note{font-size:12px;margin-top:16px}.print{margin-bottom:18px;padding:8px 16px}@page{size:A4 landscape;margin:12mm}@media print{body{margin:0;font-size:10px}.print{display:none}th,td{padding:4px}h1{font-size:17px}} </style></head><body>");
        if (printButton) output.Append("<button class='print' onclick='window.print()'>Imprimir / Guardar PDF</button>");
        output.Append("<h1>").Append(E(document.Title)).Append("</h1><p><strong>").Append(E(document.Company));
        output.Append("</strong></p><p>Período: ").Append(E(document.Period)).Append("</p><p>").Append(E(document.Options)).Append("</p><table><thead><tr>");
        foreach (var header in document.Headers) output.Append("<th scope='col'>").Append(E(header)).Append("</th>");
        output.Append("</tr></thead><tbody>");
        foreach (var row in document.Rows)
        {
            output.Append(row.Summary ? "<tr class='summary'>" : "<tr>");
            foreach (var cell in row.Cells)
                output.Append(cell.Numeric ? "<td class='number'>" : "<td>").Append(E(cell.Display)).Append("</td>");
            output.Append("</tr>");
        }
        output.Append("</tbody></table><p class='note'>").Append(E(document.Note)).Append("</p></body></html>");
        return output.ToString();
    }
}
