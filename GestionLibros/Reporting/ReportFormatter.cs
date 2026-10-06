using System.Globalization;
using System.Net;
using System.Text;
using GestionLibros.Models;

namespace GestionLibros.Reporting;

// Parentheses: how a negative amount prints (the GL shows "Saldo Anterior" in red without them).
public record ReportCell(string Value, bool Numeric = false, bool Parentheses = true)
{
    public static ReportCell Text(string text) => new(text);
    public static ReportCell Money(long cents, bool parentheses = true) => new((cents / 100m).ToString("F2", CultureInfo.InvariantCulture), true, parentheses);
    public string Display => Numeric && decimal.TryParse(Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
        ? amount.ToString("N2", CultureInfo.CurrentCulture) : Value;
}
// Indent: account level shown as a left margin on the description (column 1).
// Heading: parent account printed with its number and name only, like the GL listings.
public record ReportRow(IReadOnlyList<ReportCell> Cells, bool Summary = false, int Indent = 0, bool Heading = false);
public record ReportDocument(string Title, string Company, string Period, string Options,
    IReadOnlyList<string> Headers, IReadOnlyList<ReportRow> Rows, string Note);

public static class ReportFormatter
{
    private static readonly string[] MonthNames = ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio",
        "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"];
    private static readonly string[] MonthAbbreviations = ["ENE", "FEB", "MAR", "ABR", "MAY", "JUN", "JUL", "AGO", "SEP", "OCT", "NOV", "DIC"];
    private static readonly CultureInfo PrintNumbers = CultureInfo.GetCultureInfo("en-US");
    private static ReportCell T(string value) => ReportCell.Text(value);
    private static ReportCell M(long value, bool parentheses = true) => ReportCell.Money(value, parentheses);

    public static string MonthPeriod(int year, int month) => $"{MonthNames[month - 1]} / {year}";

    // Same layout as the GL2000 "Balanza de Comprobación": parents as headings, detail accounts with amounts.
    public static ReportDocument TrialBalance(TrialBalanceReport report)
    {
        var rows = report.Rows.Select(b => new ReportRow(new[] { T(b.Code), T(b.Description),
            M(b.Opening, false), M(b.Debits), M(b.Credits), M(b.Closing) },
            Indent: Math.Max(0, b.Level - 1), Heading: !b.IsDetail && b.Level < report.MaximumLevel)).ToList();
        var totals = report.Totals;
        rows.Add(new(new[] { T(""), T("Totales"), M(totals.OpeningDebit - totals.OpeningCredit, false), M(totals.Debits),
            M(totals.Credits), M(totals.ClosingDebit - totals.ClosingCredit) }, true));
        return new("Balanza de Comprobación", report.CompanyName, MonthPeriod(report.Year, report.Month),
            report.MaximumLevel < 9 ? $"Nivel máximo: {report.MaximumLevel}" : "",
            new[] { "Cuenta", "Descripción", "Saldo Anterior", "Cargos", "Créditos", "Saldo Actual" }, rows,
            "Saldo deudor positivo y acreedor negativo. Los totales suman solo cuentas de detalle. " +
            (totals.IsBalanced ? "Cargos y créditos iguales." : "Hay diferencias: revise los movimientos."));
    }

    // Balance General: Activo (codes 1…), Pasivo (2…) and Capital (3…) at the end of the month, each in its
    // natural sign, plus the Utilidad o Pérdida del Ejercicio from the result accounts so both sides balance.
    public static ReportDocument BalanceSheet(TrialBalanceReport report)
    {
        var rows = new List<ReportRow>();
        long Section(char digit, string title, int sign)
        {
            var accounts = report.Rows.Where(b => b.Code.StartsWith(digit)).ToList();
            rows.Add(new([T(""), T(title), T("")], Heading: true));
            rows.AddRange(accounts.Select(b => new ReportRow([T(b.Code), T(b.Description), M(sign * b.Closing)],
                Indent: Math.Max(0, b.Level - 1))));
            var total = accounts.Where(b => b.IsDetail).Sum(b => sign * b.Closing);
            rows.Add(new([T(""), T("Total " + title), M(total)], true));
            rows.Add(new([T(""), T(""), T("")]));
            return total;
        }
        var assets = Section('1', "ACTIVO", 1);
        var liabilities = Section('2', "PASIVO", -1);
        var capital = Section('3', "CAPITAL", -1);
        var result = -report.Rows.Where(b => b.IsDetail && Data.AppDatabase.IsResultAccount(b.Code)).Sum(b => b.Closing);
        rows.Add(new([T(""), T(result >= 0 ? "Utilidad del Ejercicio" : "Pérdida del Ejercicio"), M(result)]));
        rows.Add(new([T(""), T("Total Pasivo y Capital"), M(liabilities + capital + result)], true));
        var note = assets == liabilities + capital + result ? "Activo igual a Pasivo más Capital." : "Activo y Pasivo más Capital no coinciden: revise el catálogo y los movimientos.";
        return new("Balance General", report.CompanyName, $"Al cierre de {MonthPeriod(report.Year, report.Month)}",
            report.MaximumLevel < 9 ? $"Nivel máximo: {report.MaximumLevel}" : "", new[] { "Cuenta", "Descripción", "Saldo" }, rows,
            "Activo deudor positivo; Pasivo y Capital acreedores positivos. La utilidad o pérdida se calcula con las cuentas de resultados (códigos que no empiezan con 1, 2 o 3). " + note);
    }

    // Estado de Resultados: ingresos (codes 4…) and costos / gastos (5… and above), month and year to date.
    public static ReportDocument IncomeStatement(IncomeStatementReport report)
    {
        var rows = new List<ReportRow>();
        (long Month, long Year) Section(Func<string, bool> belongs, string title, int sign)
        {
            var accounts = report.Rows.Where(r => belongs(r.Code)).ToList();
            rows.Add(new([T(""), T(title), T(""), T("")], Heading: true));
            rows.AddRange(accounts.Select(r => new ReportRow([T(r.Code), T(r.Description), M(sign * r.Month), M(sign * r.YearToDate)],
                Indent: Math.Max(0, r.Level - 1))));
            var detail = accounts.Where(r => r.IsDetail).ToList();
            (long, long) total = (detail.Sum(r => sign * r.Month), detail.Sum(r => sign * r.YearToDate));
            rows.Add(new([T(""), T("Total " + title), M(total.Item1), M(total.Item2)], true));
            rows.Add(new([T(""), T(""), T(""), T("")]));
            return total;
        }
        var income = Section(code => code.StartsWith('4'), "INGRESOS", -1);
        var expenses = Section(code => !code.StartsWith('4'), "COSTOS Y GASTOS", 1);
        var month = income.Month - expenses.Month; var year = income.Year - expenses.Year;
        rows.Add(new([T(""), T(year >= 0 ? "UTILIDAD DEL EJERCICIO" : "PÉRDIDA DEL EJERCICIO"), M(month), M(year)], true));
        return new("Estado de Resultados", report.CompanyName, MonthPeriod(report.Year, report.MonthNumber),
            report.MaximumLevel < 9 ? $"Nivel máximo: {report.MaximumLevel}" : "", new[] { "Cuenta", "Descripción", "Del Mes", "Acumulado" }, rows,
            "Ingresos y costos/gastos en positivo. Acumulado: de enero al mes del reporte. Cuentas de resultados: códigos que no empiezan con 1, 2 o 3.");
    }

    // Impresión de Pólizas: each póliza with its header, movements and Sumas Iguales.
    public static ReportDocument Policies(string company, int year, int fromMonth, int throughMonth, IReadOnlyList<PolicyPrint> policies)
    {
        var rows = new List<ReportRow>();
        foreach (var policy in policies)
        {
            var j = policy.Journal;
            if (rows.Count > 0) rows.Add(new([T(""), T(""), T(""), T(""), T("")]));
            rows.Add(new([T($"{j.Type}  {j.Reference}"), T($"{j.Date:d/M/yyyy}   {policy.TypeName}".Trim()), T(j.Concept), T(""), T("")], Heading: true));
            rows.AddRange(policy.Lines.Select(l => new ReportRow([T(l.AccountCode), T(l.AccountName), T(l.Concept), M(l.Debits), M(l.Credits)])));
            var debits = policy.Lines.Sum(l => l.Debits); var credits = policy.Lines.Sum(l => l.Credits);
            rows.Add(new([T(""), T(""), T(debits == credits ? "Sumas Iguales :" : "Diferencia :"), M(debits), M(credits)], true));
        }
        var period = fromMonth == throughMonth ? MonthPeriod(year, fromMonth) : $"{MonthNames[fromMonth - 1]}  -  {MonthNames[throughMonth - 1]}  /  {year}";
        return new("Impresión de Pólizas", company, period, $"{policies.Count} pólizas",
            new[] { "Cuenta", "Descripción", "Concepto", "Cargos", "Créditos" }, rows, "Pólizas capturadas en FREDI.");
    }

    private static string Range(string from, string through) => from.Length == 0 && through.Length == 0 ? "Todas las cuentas"
        : $"Cuentas: {(from.Length == 0 ? "inicio" : from)} a {(through.Length == 0 ? "final" : through)}";

    // "Reporte de Saldos de Cuentas": only the current balance of the accounts between two codes
    // (inclusive, either end optional). Parent accounts print their totals; the total adds detail accounts.
    public static ReportDocument AccountBalances(TrialBalanceReport report, string from, string through)
    {
        bool InRange(string code) => (from.Length == 0 || string.CompareOrdinal(code, from) >= 0)
            && (through.Length == 0 || string.CompareOrdinal(code, through) <= 0);
        var selected = report.Rows.Where(b => InRange(b.Code)).ToList();
        var rows = selected.Select(b => new ReportRow(new[] { T(b.Code), T(b.Description), M(b.Closing) },
            Indent: Math.Max(0, b.Level - 1))).ToList();
        rows.Add(new(new[] { T(""), T("Total"), M(selected.Where(b => b.IsDetail).Sum(b => b.Closing)) }, true));
        var range = Range(from, through);
        return new("Reporte de Saldos de Cuentas", report.CompanyName, MonthPeriod(report.Year, report.Month),
            report.MaximumLevel < 9 ? $"{range}. Nivel máximo: {report.MaximumLevel}" : range,
            new[] { "Cuenta", "Descripción", "Saldo Actual" }, rows,
            "Saldo deudor positivo y acreedor negativo. Las cuentas superiores incluyen sus subcuentas; el total suma solo cuentas de detalle del rango.");
    }

    // A balance printed in the Cargos column when deudor and in Créditos when acreedor, as the GL Mayor does.
    private static ReportCell[] Side(long amount) => amount >= 0 ? [M(amount), T("")] : [T(""), M(-amount)];

    // GL2000 "Reporte de Mayor": per account, SALDO ANTERIOR, each partida, Total Cargos / Créditos and
    // SALDO ACTUAL; accounts without movements in the period print only their SALDO ACTUAL.
    public static ReportDocument LedgerRange(LedgerRangeReport report)
    {
        var rows = new List<ReportRow>();
        foreach (var account in report.Accounts)
        {
            if (rows.Count > 0) rows.Add(new([T(""), T(""), T(""), T(""), T(""), T(""), T("")]));
            if (account.Movements.Count == 0)
            {
                rows.Add(new([T(account.Code), T(account.Description), T(""), T(""), T("SALDO ACTUAL:"), .. Side(account.Closing)]));
                continue;
            }
            rows.Add(new([T(account.Code), T(account.Description), T(""), T(""), T("SALDO ANTERIOR:"), .. Side(account.Opening)]));
            rows.AddRange(account.Movements.Select(m => new ReportRow([T(""), T(""), T(m.Date.ToString("d/M/yyyy")),
                T($"{m.Type}  {m.Reference}".Trim()), T(m.Concept), M(m.Debits), M(m.Credits)])));
            rows.Add(new([T(""), T(""), T(""), T(""), T("Total Cargos / Créditos :"), M(account.Debits), M(account.Credits)], true));
            rows.Add(new([T(""), T(""), T(""), T(""), T("SALDO ACTUAL:"), .. Side(account.Closing)]));
        }
        var period = $"{MonthNames[report.FromMonth - 1]}  -  {MonthNames[report.ThroughMonth - 1]}  /  {report.Year}";
        var range = report.FromCode.Length == 0 && report.ThroughCode.Length == 0 ? "" : Range(report.FromCode, report.ThroughCode);
        return new("Reporte de Mayor", report.CompanyName, period, range,
            new[] { "Cuenta", "Descripción", "Fecha", "Referencia", "Concepto", "Cargos", "Créditos" }, rows,
            "Saldos: deudor en Cargos, acreedor en Créditos. Las partidas son las pólizas capturadas en FREDI; " +
            "los importes importados del GL2000 aparecen como un acumulado por mes.");
    }

    public static ReportDocument Ledger(LedgerReport report)
    {
        var rows = new List<ReportRow> { new(new[] { T(""), T(""), T(report.AccountCode), T("Saldo inicial"), M(0), M(0), M(report.Opening) }, true) };
        rows.AddRange(report.Rows.Select(r => new ReportRow(new[] { T(r.Date.ToString("dd/MM/yyyy")),
            T($"{r.Type}-{r.Reference}"), T(r.AccountCode), T(r.Concept), M(r.Debits), M(r.Credits), M(r.Balance) })));
        rows.Add(new(new[] { T("TOTAL"), T(""), T(""), T("Movimientos del período / saldo final"), M(report.Debits), M(report.Credits), M(report.Closing) }, true));
        return new("Mayor de Cuenta", report.CompanyName, $"{report.From:dd/MM/yyyy} al {report.Through:dd/MM/yyyy}",
            $"Cuenta: {report.AccountCode} - {report.AccountName}. Subcuentas: {(report.IncludesChildren ? "incluidas" : "excluidas")}",
            new[] { "Fecha", "Póliza", "Cuenta", "Concepto", "Cargos", "Créditos", "Saldo" }, rows,
            "Saldo deudor positivo y acreedor negativo. El saldo inicial incluye movimientos anteriores al rango. " +
            "Orden: fecha, tipo, referencia, identificador de póliza y movimiento. Solo incluye pólizas registradas en FREDI.");
    }

    public static ReportDocument Balances(string company, int year, int month, string filter, IReadOnlyList<BalanceRow> balances)
    {
        return new("Consulta de Cuentas", company, MonthPeriod(year, month),
            string.IsNullOrWhiteSpace(filter) ? "" : $"Filtro: {filter}",
            new[] { "Cuenta", "Descripción", "Saldo Ini", "Cargos", "Créditos", "Saldo Actual" },
            balances.Select(b => new ReportRow(new[] { T(b.Code), T(b.Description), M(b.Opening), M(b.Debits), M(b.Credits), M(b.Closing) },
                Indent: Math.Max(0, b.Level - 1))).ToList(),
            "Saldo deudor positivo y acreedor negativo. Las cuentas superiores incluyen sus descendientes; no sume las filas entre sí.");
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

    // Printed amounts: zero prints blank and negatives print red, as in the GL2000 listings.
    // Total rows keep their zeros ("0.00"), like the GL Total Cargos / Créditos line.
    private static (string Text, bool Negative) PrintAmount(ReportCell cell, bool keepZero = false)
    {
        if (!decimal.TryParse(cell.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) return (cell.Value, false);
        if (amount == 0) return (keepZero ? "0.00" : "", false);
        var text = Math.Abs(amount).ToString("N2", PrintNumbers);
        return (amount < 0 && cell.Parentheses ? $"({text})" : text, amount < 0);
    }

    // GL2000 print layout for every report: company, title, period and print date, grey column bar,
    // level indentation and "Pag. N" at the foot of each printed page. autoPrint opens the print dialog.
    public static string Html(ReportDocument document, bool monochrome = false, bool printButton = true, bool autoPrint = false, DateTime? printed = null)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);
        var date = printed ?? DateTime.Now;
        var output = new StringBuilder("<!doctype html><html lang='es'><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>");
        output.Append("<title>").Append(E(document.Title)).Append("</title><style>");
        // Every text size is explicit (table cells included) so the page looks the same in the browser,
        // the print and the in-app preview, whatever rendering mode the viewer uses.
        output.Append("body{font:10.5px Arial,sans-serif;color:#000;background:#fff;margin:0}");
        output.Append(".page{max-width:7.6in;margin:0 auto;padding:28px 24px}");
        output.Append("@media screen{body{background:#808080}.page{background:#fff;margin:12px auto;box-shadow:2px 2px 6px #333;min-height:9in}}");
        output.Append(".company{font-weight:bold;font-size:13px;margin:0 0 2px 22%}.title{font-weight:bold;font-size:16px;margin:0 0 14px 22%}");
        output.Append(".period{display:flex;align-items:flex-end}.period strong{flex:1;text-align:center;font-size:16px}.period span{font-weight:bold;font-size:10px;margin-left:12px}");
        output.Append(".options{font-size:10px;margin:4px 0 0}");
        output.Append("table{width:100%;border-collapse:collapse;margin-top:8px;font:10.5px Arial,sans-serif}");
        output.Append("th{background:").Append(monochrome ? "#e4e4e4" : "#c0c0c0").Append(";border-top:1px solid #000;border-bottom:1px solid #000;padding:1px 6px;text-align:left;font:bold 10.5px Arial,sans-serif}th:first-child{border-left:1px solid #000}th:last-child{border-right:1px solid #000}");
        output.Append("th.number,td.number,td.label{text-align:right}td{padding:0 6px;font:bold 10px/1.25 Arial,sans-serif;white-space:nowrap;vertical-align:top}");
        output.Append("td.text{white-space:normal;overflow-wrap:anywhere}.negative{color:").Append(monochrome ? "#000" : "#e00000").Append("}");
        output.Append("tr.gap td{height:12px}tr.summary td{padding-top:2px}tr.summary td.number{border-top:1px solid #000}thead{display:table-header-group}tr{break-inside:avoid}");
        output.Append(".print{margin:0 0 12px;padding:6px 14px}");
        output.Append("@page{size:letter portrait;margin:12mm 12mm 16mm;@bottom-center{content:'Pag.  ' counter(page);font:bold 10px Arial}}");
        output.Append("@media print{.page{padding:0;max-width:none}.print{display:none}}</style>");
        if (autoPrint) output.Append("<script>window.addEventListener('load',()=>window.print())</script>");
        output.Append("</head><body><div class='page'>");
        if (printButton) output.Append("<button class='print' onclick='window.print()'>Imprimir / Guardar PDF</button>");
        output.Append("<p class='company'>").Append(E(document.Company)).Append("</p><p class='title'>").Append(E(document.Title)).Append("</p>");
        output.Append("<div class='period'><strong>").Append(E(document.Period)).Append("</strong><span>Fecha&nbsp; ")
            .Append(MonthAbbreviations[date.Month - 1]).Append(' ').Append(date.Day).Append(',').Append(date.Year).Append("</span></div>");
        if (!string.IsNullOrWhiteSpace(document.Options)) output.Append("<p class='options'>").Append(E(document.Options)).Append("</p>");
        output.Append("<table><thead><tr>");
        var numeric = document.Headers.Select((_, i) => document.Rows.Any(r => i < r.Cells.Count && r.Cells[i].Numeric)).ToArray();
        for (var i = 0; i < document.Headers.Count; i++)
            output.Append(numeric[i] ? "<th scope='col' class='number'>" : "<th scope='col'>").Append(E(document.Headers[i])).Append("</th>");
        output.Append("</tr></thead><tbody>");
        foreach (var row in document.Rows)
        {
            // An all-blank row is a separator (e.g. between accounts in the Mayor) and keeps one line of height.
            var gap = row.Cells.All(c => c.Value.Length == 0);
            output.Append(row.Summary ? "<tr class='summary'>" : gap ? "<tr class='gap'>" : "<tr>");
            for (var i = 0; i < row.Cells.Count; i++)
            {
                var cell = row.Cells[i];
                if (cell.Numeric)
                {
                    var (text, negative) = row.Heading ? ("", false) : PrintAmount(cell, row.Summary);
                    output.Append(negative ? "<td class='number negative'>" : "<td class='number'>").Append(E(text)).Append("</td>");
                }
                else if (cell.Value.EndsWith(':'))
                    // Captions like "SALDO ANTERIOR:" sit against the amounts, as in the GL Mayor.
                    output.Append("<td class='label'>").Append(E(cell.Value)).Append("</td>");
                else
                {
                    output.Append(i == 1 && row.Indent > 0 ? $"<td class='text' style='padding-left:{6 + row.Indent * 15}px'>" : "<td class='text'>")
                        .Append(E(cell.Value)).Append("</td>");
                }
            }
            output.Append("</tr>");
        }
        output.Append("</tbody></table></div></body></html>");
        return output.ToString();
    }
}
