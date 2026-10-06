using System.Diagnostics;
using System.Text;
using GestionLibros.Data;

// Restablece la contraseña de un usuario de FREDI en esta computadora.
// La contraseña se escribe aquí, oculta; nunca se muestra ni se guarda en texto.
Console.OutputEncoding = Encoding.UTF8;
var path = args.Length > 0 ? args[0] : Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "User Name", "com.fredi.contabilidad", "Data", "fredi-pruebas-v1.db3");
Console.WriteLine("FREDI · Restablecer contraseña");
Console.WriteLine($"Base: {path}");
if (!File.Exists(path)) { Console.WriteLine("No se encontró la base de FREDI. Indique la ruta como argumento."); return 1; }
if (Process.GetProcessesByName("GestionLibros").Length > 0) { Console.WriteLine("Cierre FREDI antes de continuar."); return 1; }

Console.Write("Usuario (Enter = cesarportillo): ");
var username = Console.ReadLine()?.Trim();
if (string.IsNullOrEmpty(username)) username = "cesarportillo";
var password = ReadHidden("Nueva contraseña (mínimo 10 caracteres): ");
if (password != ReadHidden("Confirme la nueva contraseña: ")) { Console.WriteLine("Las contraseñas no coinciden. No se cambió nada."); return 1; }

// Safety copy first, so the change can be undone by restoring this file.
var backup = Path.Combine(Path.GetDirectoryName(path)!, $"fredi-pruebas-v1.antes-de-restablecer-{DateTime.Now:yyyyMMdd-HHmmss}.db3");
File.Copy(path, backup);
try
{
    await new AppDatabase(path).ResetPassword(username, password);
    Console.WriteLine($"Listo. La contraseña de \"{username.ToLowerInvariant()}\" se cambió y sus sesiones guardadas se cerraron.");
    Console.WriteLine($"Respaldo previo: {backup}");
    return 0;
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"No se cambió nada: {ex.Message}");
    File.Delete(backup);
    return 1;
}

static string ReadHidden(string prompt)
{
    Console.Write(prompt);
    var text = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) { text.Length--; Console.Write("\b \b"); } continue; }
        if (!char.IsControl(key.KeyChar)) { text.Append(key.KeyChar); Console.Write('*'); }
    }
    Console.WriteLine();
    return text.ToString();
}
