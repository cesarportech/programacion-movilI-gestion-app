using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GestionLibros.Data;

public record CloudConfiguration(string BaseUrl)
{
    public const string DefaultUrl = "https://efekzvmgnicztweluyim.supabase.co/functions/v1/fredi-api/";
    public static CloudConfiguration Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<CloudConfiguration>(File.ReadAllText(path)) ?? throw new InvalidOperationException("Configuración API inválida.")
        : new(DefaultUrl);
    public Uri Validate()
    {
        if (!Uri.TryCreate(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var url) || url.Scheme != "https" ||
            url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0)
            throw new InvalidOperationException("La dirección base debe ser HTTPS, sin claves, parámetros ni fragmentos.");
        return url;
    }
}

// The cloud rejected the username/password (as opposed to a connection or server failure).
public sealed class CloudAuthenticationException(string message) : InvalidOperationException(message);

// All HTTP calls and route names live here. No view builds Supabase URLs or carries a service key.
public sealed class CloudApi : IDisposable
{
    public const string LoginRoute = "v1/auth/login";
    public const string RestoreRoute = "v1/restore";
    public const string LogoutRoute = "v1/auth/logout";
    public const string UploadRoute = "v1/sync";
    public const string StatusRoute = "v1/status";
    public const string CompaniesRoute = "v1/contabilidades";
    public const string CatalogsRoute = "v1/catalogos";
    private readonly HttpClient http;
    private string? token;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public CloudApi(CloudConfiguration configuration, HttpMessageHandler? handler = null)
    {
        http = handler == null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) : new HttpClient(handler);
        http.BaseAddress = configuration.Validate(); http.Timeout = TimeSpan.FromSeconds(45);
    }
    private async Task<T> Send<T>(string route, string json, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellation);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException("Hay cambios de otra instalación en la nube. El envío queda pendiente; debe reconciliarse antes de subirlo. No se sobrescribió la nube.");
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new CloudAuthenticationException("La nube rechazó el acceso. Verifique la contraseña y que el maestro haya acumulado los usuarios y permisos actuales.");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("Demasiados intentos de acceso. Espere 15 minutos antes de volver a intentar.");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"La nube no confirmó el envío (HTTP {(int)response.StatusCode}). Los cambios continúan pendientes en este equipo.");
        return JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(cancellation), JsonOptions)
            ?? throw new InvalidOperationException("La nube respondió sin confirmación válida.");
    }
    // A null dataset is for a new installation: the cloud identifies it by the (unique) username and returns it.
    public async Task<string> Login(string? dataset, string username, string password, CancellationToken cancellation = default)
    {
        token = null;
        var result = await Send<LoginResult>(LoginRoute, JsonSerializer.Serialize(new { dataset, username, password }), cancellation);
        if (string.IsNullOrEmpty(result.Token)) throw new InvalidOperationException("La nube no entregó una sesión válida.");
        token = result.Token;
        return result.Dataset ?? dataset ?? throw new InvalidOperationException("La nube no identificó el conjunto de datos.");
    }
    public async Task<CloudSnapshot> Restore(CancellationToken cancellation = default)
    {
        try { return (await Read(RestoreRoute, cancellation)).Deserialize<CloudSnapshot>(JsonOptions) ?? throw new InvalidOperationException("La nube respondió sin datos."); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("HTTP 404"))
        { throw new InvalidOperationException("El servidor de la nube todavía no permite descargar datos. Actualice la función fredi-api y gateway.sql."); }
    }
    public async Task<long> Upload(CloudPending pending, CancellationToken cancellation = default)
    {
        if (token == null) throw new InvalidOperationException("Inicie sesión en la nube.");
        var result = await Send<UploadResult>(UploadRoute, pending.Payload, cancellation);
        if (result.RequestId != pending.RequestId) throw new InvalidOperationException("La nube confirmó un envío diferente. Se conserva el pendiente.");
        return result.Revision;
    }
    public Task<JsonElement> Companies(CancellationToken cancellation = default) => Read(CompaniesRoute, cancellation);
    public Task<JsonElement> Company(int id, CancellationToken cancellation = default) => id > 0
        ? Read(CompaniesRoute + "/" + id.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellation)
        : throw new ArgumentOutOfRangeException(nameof(id));
    public Task<JsonElement> Catalogs(CancellationToken cancellation = default) => Read(CatalogsRoute, cancellation);
    private async Task<JsonElement> Read(string route, CancellationToken cancellation)
    {
        if (token == null) throw new InvalidOperationException("Inicie sesión en la nube.");
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellation);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"La nube rechazó la consulta (HTTP {(int)response.StatusCode}).");
        return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync(cancellation));
    }
    public async Task Logout()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { if (token != null) await Send<JsonElement>(LogoutRoute, "{}", timeout.Token); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException) { }
        finally { token = null; }
    }
    public void Dispose() { token = null; http.Dispose(); }
    private record LoginResult(string Token, string? Dataset);
    private record UploadResult(string RequestId, long Revision);
}
