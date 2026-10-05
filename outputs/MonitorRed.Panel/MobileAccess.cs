using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed class MobileAccess
{
    private readonly string[] accessCodes;
    private readonly EventRepository? events;
    private readonly OwnerNotifications? owner;
    private readonly object sessionGate = new();
    private readonly string sessionsPath;
    private Dictionary<int, MobileSession> sessions = new();
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
        sessionsPath = Path.Combine(dataDir, "mobile-sessions.dpapi");
        if (File.Exists(sessionsPath))
        {
            var saved = JsonSerializer.Deserialize<MobileSession[]>(ProtectedData.Unprotect(File.ReadAllBytes(sessionsPath), null, DataProtectionScope.CurrentUser))
                ?? throw new InvalidDataException("No se pudieron leer las sesiones de consulta.");
            if (saved.Length > accessCodes.Length || saved.Select(s => s.Slot).Distinct().Count() != saved.Length ||
                saved.Any(s => s.Slot < 0 || s.Slot >= accessCodes.Length || s.TokenHash is null || s.TokenHash.Length != 64 || s.TokenHash.Any(c => !char.IsAsciiHexDigit(c))))
                throw new InvalidDataException("Las sesiones de consulta no son válidas. Restaurar su respaldo.");
            sessions = saved.ToDictionary(s => s.Slot);
        }
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
        lock (sessionGate)
        {
            if (FindSession(cookie) is null) return false;
            // Browser storage has its own limits; renew the cookie while the user is active.
            if (!context.Response.HasStarted) SetCookie(context, cookie!);
            return true;
        }
    }
    private MobileSession? FindSession(string? token)
    {
        if (token is null || token.Length != 64 || token.Any(c => !char.IsAsciiHexDigit(c))) return null;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return sessions.Values.FirstOrDefault(s => s.TokenHash == hash);
    }
    private static void SetCookie(HttpContext context, string token) => context.Response.Cookies.Append("VisionMobile", token, new CookieOptions
    {
        HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = context.Request.IsHttps,
        MaxAge = TimeSpan.FromDays(365), Path = "/", IsEssential = true
    });
    private void SaveSessions(Dictionary<int, MobileSession> next)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var plain = JsonSerializer.SerializeToUtf8Bytes(next.Values.ToArray());
        try
        {
            var temporary = sessionsPath + ".tmp";
            File.WriteAllBytes(temporary, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
            File.Move(temporary, sessionsPath, true);
            sessions = next;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public object[] AccessStatus()
    {
        lock (sessionGate)
            return Enumerable.Range(0, accessCodes.Length).Select(slot =>
            {
                sessions.TryGetValue(slot, out var session);
                return (object)new { slot = slot + 1, inUse = session is not null,
                    connectedAtUtc = session?.ConnectedAtUtc, device = session?.Device };
            }).ToArray();
    }
    public IResult Release(int slot)
    {
        if (slot < 1 || slot > accessCodes.Length) return Results.BadRequest(new { error = "Acceso inválido." });
        bool released;
        lock (sessionGate)
        {
            var next = new Dictionary<int, MobileSession>(sessions);
            released = next.Remove(slot - 1);
            if (released) SaveSessions(next);
        }
        if (released) events?.Add(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, "access", "info", "mobile_access_released",
            name: "Consulta móvil", description: $"El editor local liberó el acceso {slot}. Su sesión anterior ya no es válida.");
        return Results.Ok(new { released });
    }
    public IResult Login(HttpContext context, MobileLogin input)
    {
        if (IsAuthenticated(context)) return Results.Ok(new { connected = true });
        var now = DateTimeOffset.UtcNow;
        foreach (var attempt in attempts.Where(s => s.Value.Until < now)) attempts.TryRemove(attempt.Key, out _);
        var remote = context.Connection.RemoteIpAddress?.ToString() ?? "local";
        var previous = attempts.GetValueOrDefault(remote);
        if (previous.Count >= 5 && previous.Until > now) return Results.Json(new { error = "Varios intentos incorrectos. Espera 15 minutos." }, statusCode: 429);
        var code = string.Concat((input.Code ?? "").Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
        var codeBytes = Encoding.UTF8.GetBytes(code);
        var slot = -1;
        for (var i = 0; i < accessCodes.Length; i++)
            if (CryptographicOperations.FixedTimeEquals(codeBytes, Encoding.UTF8.GetBytes(accessCodes[i]))) slot = i;
        if (slot < 0)
        {
            attempts[remote] = (previous.Count + 1, now.AddMinutes(15));
            events?.Add(Guid.NewGuid().ToString(),now,"access","warning","login_rejected",remote,"Consulta móvil",to:"rejected",description:"Código incorrecto. No se registra el código introducido.");
            return Results.Json(new { error = "Código de acceso incorrecto." }, statusCode: 401);
        }
        attempts.TryRemove(remote, out _);
        bool ownerQueued;
        lock (sessionGate)
        {
            // Recheck inside the same lock as reservation: simultaneous logins cannot both win.
            if (FindSession(context.Request.Cookies["VisionMobile"]) is not null) return Results.Ok(new { connected = true });
            if (sessions.ContainsKey(slot)) return Results.Json(new { error = "Este código ya está en uso en otro dispositivo o navegador. Cierra allí la sesión o pide al administrador liberar el acceso." }, statusCode: 409);
            ownerQueued = owner?.MobileLogin(remote, context.Request.Headers.UserAgent.ToString(), now) == true;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var agent = context.Request.Headers.UserAgent.ToString();
            var device = agent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iPad" :
                agent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "iPhone" :
                agent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" :
                agent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" : "Navegador de consulta";
            var next = new Dictionary<int, MobileSession>(sessions) { [slot] = new(slot, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))), now, device) };
            SaveSessions(next);
            SetCookie(context, token);
        }
        events?.Add(Guid.NewGuid().ToString(),now,"access","info","login_success",remote,"Consulta móvil",to:"connected",description:"Inicio de sesión con código compartido. No identifica a una persona. " + (owner?.Enabled != true ? "Avisos al autor desactivados." : ownerQueued ? "Aviso al autor registrado en la cola." : "Aviso al autor omitido: ya se avisó de este dispositivo en las últimas 24 h o se alcanzó el límite diario."));
        return Results.Ok(new { connected = true });
    }
    public IResult Logout(HttpContext context)
    {
        lock (sessionGate)
        {
            if (FindSession(context.Request.Cookies["VisionMobile"]) is { } session)
            {
                var next = new Dictionary<int, MobileSession>(sessions);
                next.Remove(session.Slot);
                SaveSessions(next);
            }
        }
        context.Response.Cookies.Delete("VisionMobile", new CookieOptions { Path = "/" });
        events?.Add(Guid.NewGuid().ToString(),DateTimeOffset.UtcNow,"access","info","logout",context.Connection.RemoteIpAddress?.ToString()??"","Consulta móvil",to:"disconnected",description:"Cierre de sesión solicitado desde la consulta móvil.");
        return Results.Ok(new { connected = false });
    }
}
public sealed record MobileSession(int Slot, string TokenHash, DateTimeOffset ConnectedAtUtc, string Device);
public sealed record MobileLogin(string? Code);
