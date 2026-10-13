using System.Globalization;

namespace GestionLibros.Data;

// Evaluates the Valor field of a movement: numbers with "." decimals, + - * / and parentheses.
// Commas are thousands separators. Returns null for anything else.
public static class AmountExpression
{
    public static decimal? TryPostingShortcut(string? text)
    {
        var input = (text ?? "").Trim();
        if (input.Length < 2 || input[^1] is not ('+' or '-')) return null;
        var amount = TryEvaluate(input[..^1]);
        if (amount == null || amount == 0 || amount == decimal.MinValue) return null;
        return input[^1] == '+' ? Math.Abs(amount.Value) : -Math.Abs(amount.Value);
    }
    public static decimal? TryEvaluate(string? text)
    {
        var s = (text ?? "").Replace(",", "").Replace(" ", "");
        if (s.Length == 0) return 0;
        var pos = 0;
        try
        {
            var result = Sum();
            return pos == s.Length ? result : null;
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or DivideByZeroException) { return null; }

        decimal Sum()
        {
            var total = Product();
            while (pos < s.Length && s[pos] is '+' or '-') { var op = s[pos++]; var next = Product(); total = op == '+' ? total + next : total - next; }
            return total;
        }
        decimal Product()
        {
            var total = Unary();
            while (pos < s.Length && s[pos] is '*' or '/') { var op = s[pos++]; var next = Unary(); total = op == '*' ? total * next : total / next; }
            return total;
        }
        decimal Unary()
        {
            if (pos < s.Length && s[pos] is '-' or '+') { var negative = s[pos++] == '-'; var v = Unary(); return negative ? -v : v; }
            if (pos < s.Length && s[pos] == '(') { pos++; var v = Sum(); if (pos >= s.Length || s[pos++] != ')') throw new FormatException(); return v; }
            var start = pos;
            while (pos < s.Length && (char.IsDigit(s[pos]) || s[pos] == '.')) pos++;
            return decimal.Parse(s[start..pos], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }
    }
}
