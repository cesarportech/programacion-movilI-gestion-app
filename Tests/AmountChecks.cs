using GestionLibros.Data;

static class AmountChecks
{
    public static void Run()
    {
        foreach (var (input, expected) in new (string, decimal?)[] { ("100+", 100m), ("100-", -100m), ("-100+", 100m), ("(100+20)-", -120m), ("+", null), ("-", null), ("(100+", null), ("0+", null), ("10", null) })
        {
            if (AmountExpression.TryPostingShortcut(input) != expected) throw new Exception("Signed posting shortcut: " + input);
            Console.WriteLine("PASS SHORTCUT " + input);
        }
        var cases = new (string Text, decimal? Expected)[]
        {
            ("1500", 1500m), ("3,764.75", 3764.75m), ("-51338.43", -51338.43m), ("1500+250*2", 2000m),
            ("(100+15)/1.15", 100m), ("-(20+5)", -25m), (" 0.97 ", 0.97m), ("", 0m),
            ("12,a", null), ("10/0", null), ("(5+2", null), ("5..2", null), ("1e5", null),
        };
        foreach (var (text, expected) in cases)
        {
            var actual = AmountExpression.TryEvaluate(text);
            if (actual != expected) throw new Exception($"Valor '{text}': esperado {expected?.ToString() ?? "inválido"}, obtenido {actual?.ToString() ?? "inválido"}");
            Console.WriteLine($"PASS AMOUNT '{text}' = {actual?.ToString() ?? "inválido"}");
        }
        Console.WriteLine($"All {cases.Length} amount checks passed.");
    }
}

