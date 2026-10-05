using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed class MobileAccess
{
    private readonly string[] accessCodes;
    private readonly EventRepository? events;
    private readonly OwnerNotifications? owner;
    private readonly ConcurrentDictionary<string, DateTimeOffset> sessions = new();
    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Until)> attempts = new();
    public MobileAccess(string dataDir, EventRepository? events = null, OwnerNotifications? owner = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        this.events=events;
        this.owner=owner;
        var path = Path.Combine(dataDir, "mobile-access-codes.dpapi");
        if (File.Exists(path))
            accessCodes = JsonSerializer.Deserialize<string[]>(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser))
                ?? throw new InvalidDataException("No se pudieron leer los códigos de consulta.");
        else
        {
            var legacyPath = Path.Combine(dataDir, "mobile-access.dpapi");
            var primary = File.Exists(legacyPath)
                ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(legacyPath), null, DataProtectionScope.CurrentUser))
                : Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
            var codes = new List<string> { primary };
            while (codes.Count < 4)
            {
                var candidate = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
                if (!codes.Contains(candidate, StringComparer.Ordinal)) codes.Add(candidate);
            }
            accessCodes = codes.ToArray();
            ValidateCodes(accessCodes);
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(accessCodes), null, DataProtectionScope.CurrentUser));
            File.Move(temporary, path);
        }
        ValidateCodes(accessCodes);
    }
    private static void ValidateCodes(string[] codes)
    {
        if (codes.Length != 4 || codes.Distinct(StringComparer.Ordinal).Count() != 4 ||
            codes.Any(code => code is null || code.Length is < 12 or > 64 || code.Any(c => !char.IsAsciiHexDigit(c)) || code != code.ToUpperInvariant()))
            throw new InvalidDataException("El archivo de códigos de consulta no es válido. Restaurar su respaldo; no regenerar los accesos existentes.");
    }
    public string LocalAccessCode => accessCodes[0];
    public IReadOnlyList<string> LocalAccessCodes => Array.AsReadOnly(accessCodes);
    public bool IsAuthenticated(HttpContext context)
    {
        var cookie = context.Request.Cookies["VisionMobile"];
        return cookie is not null && sessions.TryGetValue(cookie, out var expires) && expires > DateTimeOffset.UtcNow;
    }
    public IResult Login(HttpContext context, MobileLogin input)
    {
        if (IsAuthenticated(context)) return Results.Ok(new { connected = true });
        var now = DateTimeOffset.UtcNow;
        foreach (var session in sessions.Where(s => s.Value < now)) sessions.TryRemove(session.Key, out _);
        foreach (var attempt in attempts.Where(s => s.Value.Until < now)) attempts.TryRemove(attempt.Key, out _);
        var remote = context.Connection.RemoteIpAddress?.ToString() ?? "local";
        var previous = attempts.GetValueOrDefault(remote);
        if (previous.Count >= 5 && previous.Until > now) return Results.Json(new { error = "Varios intentos incorrectos. Espera 15 minutos." }, statusCode: 429);
        var code = string.Concat((input.Code ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        var codeBytes = Encoding.UTF8.GetBytes(code);
        var matched = false;
        foreach (var validCode in accessCodes)
            matched |= CryptographicOperations.FixedTimeEquals(codeBytes, Encoding.UTF8.GetBytes(validCode));
        if (!matched)
        {
            attempts[remote] = (previous.Count + 1, now.AddMinutes(15));
            events?.Add(Guid.NewGuid().ToString(),now,"access","warning","login_rejected",remote,"Consulta móvil",to:"rejected",description:"Código incorrecto. No se registra el código introducido.");
            return Results.Json(new { error = "Código de acceso incorrecto." }, statusCode: 401);
        }
        attempts.TryRemove(remote, out _);
        if (sessions.Count > 1000) return Results.Json(new { error = "Límite de sesiones alcanzado." }, statusCode: 429);
        var ownerQueued = owner?.MobileLogin(remote, context.Request.Headers.UserAgent.ToString(), now) == true;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        sessions[token] = now.AddHours(24);
        context.Response.Cookies.Append("VisionMobile", token, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps,
            MaxAge = TimeSpan.FromHours(24), Path = "/", IsEssential = true
        });
        events?.Add(Guid.NewGuid().ToString(),now,"access","info","login_success",remote,"Consulta móvil",to:"connected",description:"Inicio de sesión con código compartido. No identifica a una persona. " + (owner?.Enabled != true ? "Avisos al autor desactivados." : ownerQueued ? "Aviso al autor registrado en la cola." : "Aviso al autor omitido: ya se avisó de este dispositivo en las últimas 24 h o se alcanzó el límite diario."));
        return Results.Ok(new { connected = true });
    }
    public IResult Logout(HttpContext context)
    {
        if (context.Request.Cookies["VisionMobile"] is { } token) sessions.TryRemove(token, out _);
        context.Response.Cookies.Delete("VisionMobile");
        events?.Add(Guid.NewGuid().ToString(),DateTimeOffset.UtcNow,"access","info","logout",context.Connection.RemoteIpAddress?.ToString()??"","Consulta móvil",to:"disconnected",description:"Cierre de sesión solicitado desde la consulta móvil.");
        return Results.Ok();
    }
}
public sealed record MobileLogin(string? Code);
