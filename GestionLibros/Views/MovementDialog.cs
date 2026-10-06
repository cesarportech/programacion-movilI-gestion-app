using System.Globalization;
using GestionLibros.Data;
using GestionLibros.Models;

namespace GestionLibros.Views;

// "Agregando : <tipo>-<referencia>" window floating over the journal editor. Like the GL2000 it has a
// single Valor: positive = cargo, negative = crédito.
internal sealed class MovementDialog : ClassicDialog
{
    // Impuesto sobre ventas used by the IVA buttons (Honduras ISV 15 %).
    private const decimal TaxRate = 0.15m;
    private readonly Entry account = new() { MaxLength = 40 };
    private readonly Label accountName = ClassicWorkspaceChrome.Text("");
    private readonly Entry concept = new() { MaxLength = 300 };
    private readonly Entry value = new() { MaxLength = 40, HorizontalTextAlignment = TextAlignment.End };
    private readonly Label side = ClassicWorkspaceChrome.Text("", true);
    private List<Account> detail = [];
    private Action<JournalLine>? accept;

    public MovementDialog(Page page) : base(page, 440, 124, "Movimiento",
        "Cuenta: cuenta de detalle (la lupa busca por número o nombre). Valor: positivo es cargo, negativo es crédito. " +
        "Puede escribir operaciones (por ejemplo 1500+250*2) y pulsar la calculadora o Enter. " +
        $"IVA+ agrega el {TaxRate:P0} al valor, IVA- lo quita de un valor que ya lo incluye, e IVA deja solo el impuesto del valor.")
    {
        FieldLabel("Cuenta:", 12); FieldLabel("Concepto :", 37); FieldLabel("Valor :", 63);
        Put(Field(account, Colors.White, "Cuenta"), 80, 12, 90, 20);
        Put(JournalsPage.ActionButton("", "gl_search.png", PickAccount, 28), 174, 10, 28, 24);
        accountName.TextColor = LabelBlue; accountName.LineBreakMode = LineBreakMode.TailTruncation;
        Put(accountName, 206, 12, 210, 20);
        Put(Field(concept, Colors.White, "Concepto"), 80, 37, 300, 20);
        Put(Field(value, Colors.White, "Valor"), 80, 63, 120, 20);
        Put(JournalsPage.ActionButton("", "gl_balances.png", () => { Calculate(); return Task.CompletedTask; }, 30), 204, 61, 30, 24);
        Put(TextButton("IVA -", () => Tax(v => v / (1 + TaxRate))), 240, 62, 50, 22);
        Put(TextButton("IVA +", () => Tax(v => v * (1 + TaxRate))), 294, 62, 50, 22);
        Put(TextButton("IVA", () => Tax(v => v * TaxRate)), 348, 62, 50, 22);
        Put(side, 80, 86, 120, 16);
        Put(Error, 8, 104, 410, 16);

        account.TextChanged += (_, _) => ShowAccount();
        account.Completed += (_, _) => concept.Focus();
        concept.Completed += (_, _) => value.Focus();
        value.TextChanged += (_, _) => ShowSide();
        value.Completed += async (_, _) => { if (Calculate()) await Accept(); };
    }

    public void Show(string title, List<Account> detailAccounts, JournalLine? line, string defaultConcept, Action<JournalLine> onAccept)
    {
        detail = detailAccounts; accept = onAccept;
        account.Text = detail.FirstOrDefault(a => a.Id == line?.AccountId)?.Code ?? "";
        concept.Text = line?.Concept ?? defaultConcept;
        value.Text = line == null ? "0.00" : Format((line.DebitCents - line.CreditCents) / 100m);
        ShowAccount(); ShowSide();
        Open(title, account);
    }

    private Account? Selected() => detail.FirstOrDefault(a => a.Code == (account.Text ?? "").Trim());

    private void ShowAccount()
    {
        var code = (account.Text ?? "").Trim();
        accountName.Text = code.Length == 0 ? "" : Selected()?.Description ?? "No es cuenta de detalle";
    }

    private void ShowSide()
    {
        var amount = AmountExpression.TryEvaluate(value.Text);
        side.Text = amount is > 0 ? "= Cargo" : amount is < 0 ? "= Crédito" : "";
        side.TextColor = amount is < 0 ? Gl2000Chrome.NegativeRed : LabelBlue;
    }

    private async Task PickAccount()
    {
        var typed = (account.Text ?? "").Trim();
        var matches = detail.Where(a => a.Code.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(typed, StringComparison.CurrentCultureIgnoreCase))
            .Take(60).Select(a => $"{a.Code}  {a.Description}").ToArray();
        if (matches.Length == 0) { Error.Text = "No hay cuentas de detalle que coincidan."; return; }
        var choice = await Owner.DisplayActionSheetAsync("Cuentas de detalle", "Cancelar", null, matches);
        if (choice != null && matches.Contains(choice)) account.Text = choice[..choice.IndexOf("  ", StringComparison.Ordinal)];
        concept.Focus();
    }

    private bool Calculate()
    {
        var amount = AmountExpression.TryEvaluate(value.Text);
        if (amount == null) { Error.Text = "Valor no válido. Use números y + - * / ( )."; return false; }
        value.Text = Format(amount.Value); Error.Text = "";
        return true;
    }

    private Task Tax(Func<decimal, decimal> change)
    {
        var amount = AmountExpression.TryEvaluate(value.Text);
        if (amount == null) Error.Text = "Valor no válido.";
        else value.Text = Format(decimal.Round(change(amount.Value), 2, MidpointRounding.AwayFromZero));
        return Task.CompletedTask;
    }

    protected override Task<bool> Save()
    {
        var selected = Selected() ?? throw new InvalidOperationException("Indique una cuenta de detalle.");
        var amount = AmountExpression.TryEvaluate(value.Text) ?? throw new InvalidOperationException("Valor no válido.");
        amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount == 0) throw new InvalidOperationException("El valor no puede ser cero. Positivo = cargo, negativo = crédito.");
        if (Math.Abs(amount) > 9_999_999_999.99m) throw new InvalidOperationException("Importe fuera del límite.");
        var cents = (long)(Math.Abs(amount) * 100);
        accept?.Invoke(new JournalLine { AccountId = selected.Id, Concept = (concept.Text ?? "").Trim(),
            DebitCents = amount > 0 ? cents : 0, CreditCents = amount < 0 ? cents : 0 });
        return Task.FromResult(true);
    }

    private static Button TextButton(string text, Func<Task> action)
    {
        var button = JournalsPage.FlatButton(text, action);
        button.Background = ClassicWorkspaceChrome.ToolbarBrush(); button.BorderColor = Colors.Gray; button.HeightRequest = 22;
        SemanticProperties.SetDescription(button, text);
        return button;
    }

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);
}
