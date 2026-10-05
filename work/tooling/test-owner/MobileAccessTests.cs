using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

internal static class MobileAccessTests
{
    public static async Task Run(string root)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var data = Path.Combine(root, "mobile");
        Directory.CreateDirectory(data);
        var legacyCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
        File.WriteAllBytes(Path.Combine(data, "mobile-access.dpapi"), ProtectedData.Protect(Encoding.UTF8.GetBytes(legacyCode), null, DataProtectionScope.CurrentUser));
        var mobile = new MobileAccess(data);
        var codes = mobile.LocalAccessCodes.ToArray();
        if (codes[0] != legacyCode) throw new Exception("Se reemplazó el código anterior.");
        var attempts = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() =>
        {
            var context = Context();
            return (Result: mobile.Login(context, new(codes[0])), Context: context);
        })));
        if (attempts.Count(a => Status(a.Result) == 200) != 1 || attempts.Count(a => Status(a.Result) == 409) != 23)
            throw new Exception("Un código admitió sesiones simultáneas.");
        var winner = attempts.Single(a => Status(a.Result) == 200).Context;
        var token = Cookie(winner);
        var authenticated = Context(token);
        if (!mobile.IsAuthenticated(authenticated) || Status(mobile.Login(authenticated, new(codes[0]))) != 200)
            throw new Exception("El mismo navegador no conservó su acceso.");
        var other = Context();
        if (Status(mobile.Login(other, new(codes[1]))) != 200) throw new Exception("Otro código no puede funcionar a la vez.");
        var otherToken = Cookie(other);
        var restarted = new MobileAccess(data);
        if (!restarted.LocalAccessCodes.SequenceEqual(codes) || !restarted.IsAuthenticated(Context(token)) ||
            Status(restarted.Login(Context(), new(codes[0]))) != 409)
            throw new Exception("El reinicio perdió códigos, sesión o exclusividad.");
        var sessionsPath = Path.Combine(data, "mobile-sessions.dpapi");
        var saved = JsonSerializer.Deserialize<MobileSession[]>(ProtectedData.Unprotect(File.ReadAllBytes(sessionsPath), null, DataProtectionScope.CurrentUser))!;
        if (saved.Any(s => s.TokenHash == token || s.TokenHash == otherToken)) throw new Exception("Se guardó un token sin hash.");
        File.WriteAllBytes(sessionsPath, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(saved.Select(s => s with { ConnectedAtUtc = DateTimeOffset.UtcNow.AddYears(-10) }).ToArray()), null, DataProtectionScope.CurrentUser));
        restarted = new MobileAccess(data);
        if (!restarted.IsAuthenticated(Context(token))) throw new Exception("La sesión caducó por tiempo.");
        var status = JsonSerializer.Serialize(restarted.AccessStatus());
        if (status.Contains(token) || saved.Any(s => status.Contains(s.TokenHash)) || codes.Any(status.Contains))
            throw new Exception("El estado de accesos expuso secretos.");
        if (Status(restarted.Release(0)) != 400 || Status(restarted.Release(5)) != 400) throw new Exception("Se aceptó liberar un acceso inválido.");
        restarted.Release(1);
        if (restarted.IsAuthenticated(Context(token)) || !restarted.IsAuthenticated(Context(otherToken)))
            throw new Exception("Liberar acceso no revocó únicamente su sesión.");
        var replacement = Context();
        if (Status(restarted.Login(replacement, new(codes[0]))) != 200) throw new Exception("No se pudo reutilizar el código liberado.");
        var replacementToken = Cookie(replacement);
        restarted.Logout(Context(token));
        if (!restarted.IsAuthenticated(Context(replacementToken))) throw new Exception("Una cookie revocada cerró la nueva sesión.");
        restarted.Logout(Context(replacementToken));
        restarted = new MobileAccess(data);
        if (restarted.IsAuthenticated(Context(replacementToken)) || Status(restarted.Login(Context(), new(codes[0]))) != 200)
            throw new Exception("Cerrar sesión no liberó el código de forma persistente.");
        var failed = Context();
        if (Status(restarted.Login(failed, new("incorrecto"))) != 401 || failed.Response.Headers.ContainsKey("Set-Cookie"))
            throw new Exception("El código incorrecto consiguió acceso.");
        Console.WriteLine("Correcto: código permanente, exclusividad concurrente, reinicio, sin vencimiento temporal, liberación, logout y estado sin secretos.");
    }
    private static DefaultHttpContext Context(string? token = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers.UserAgent = "Android Test";
        if (token is not null) context.Request.Headers.Cookie = "VisionMobile=" + token;
        return context;
    }
    private static int Status(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 200;
    private static string Cookie(HttpContext context) => context.Response.Headers.SetCookie.First()!.Split(';')[0].Split('=', 2)[1];
}
