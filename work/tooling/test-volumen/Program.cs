// Pruebas de protección de volumen de correo, diario JSONL y validaciones. Transporte ficticio: no envía correos.
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

// Always under the test build folder, never in the panel folder that gets published.
var root = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var failures = 0;
void Check(bool condition, string description)
{
    Console.WriteLine((condition ? "OK    " : "FALLA ") + description);
    if (!condition) failures++;
}
string Fresh(string name) { var dir = Path.Combine(root, name); Directory.CreateDirectory(dir); return Path.Combine(dir, "queue.json"); }
Incident Device(string id, string ip, string name, DateTimeOffset opened, DateTimeOffset? closed = null) => new(id, ip, name, opened, closed, "Prueba");
var t0 = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

// 1. Diario JSONL: el monitor escribe mientras el registro CSV lee, sin bloquearse.
{
    var journal = Path.Combine(root, "eventos-monitor-prueba.jsonl");
    File.WriteAllText(journal, "");
    var errors = 0;
    using var stop = new CancellationTokenSource();
    var reader = Task.Run(() =>
    {
        while (!stop.IsCancellationRequested)
            try { foreach (var _ in JournalFile.ReadLines(journal)) Thread.SpinWait(50); }
            catch (IOException) { Interlocked.Increment(ref errors); }
    });
    for (var i = 0; i < 3000; i++) await JournalFile.AppendAsync(journal, $"{{\"n\":{i}}}\n", CancellationToken.None);
    stop.Cancel(); await reader;
    var lines = JournalFile.ReadLines(journal).ToList();
    Check(errors == 0 && lines.Count == 3000, $"Diario: 3000 escrituras con lectura simultánea, {errors} conflictos de lectura");
    // Escritura con un lector abierto todo el tiempo (el caso que antes abortaba la ronda).
    using (var held = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
    {
        var ok = true;
        try { await JournalFile.AppendAsync(journal, "{\"n\":-1}\n", CancellationToken.None); } catch (IOException) { ok = false; }
        Check(ok, "Diario: el monitor escribe aunque el registro tenga el archivo abierto");
    }
    // Comportamiento anterior, para confirmar que la prueba reproduce el conflicto original.
    var oldConflict = false;
    using (var oldReader = File.OpenText(journal))
        try { await File.AppendAllTextAsync(journal, "x\n"); } catch (IOException) { oldConflict = true; }
    Check(oldConflict, "Diario: la lectura anterior (File.ReadLines) sí bloqueaba la escritura del monitor");
}

// 2. Agrupación: cambios simultáneos salen juntos después de la ventana.
{
    var outbox = new NotificationOutbox(Fresh("group"));
    for (var i = 0; i < 10; i++) outbox.Enqueue(Device($"g{i}", $"10.0.0.{i}", $"Switch {i}", t0), "down");
    Check(outbox.ClaimBatch(t0.AddSeconds(5)).Length == 0, "Agrupación: espera la ventana antes de enviar");
    var batch = outbox.ClaimBatch(t0.AddSeconds(25));
    Check(batch.Length == 10, $"Agrupación: 10 caídas simultáneas en un solo lote ({batch.Length})");
    var digest = AlertMessage.ComposeDigest(batch, outbox, t0.AddSeconds(25));
    Check(digest.Subject.StartsWith("[VISION-APODACA] Resumen de red") && digest.Subject.Contains("10 caídas"), "Resumen: asunto con prefijo y conteo: " + digest.Subject);
    Check(digest.Message.Contains("Switch 9") && digest.Message.Contains("seguían sin respuesta"), "Resumen: lista equipos y estado al cierre");

    // 3. Límite de Microsoft (429): pausa general y el intento no cuenta.
    var limited = new EmailDelivery("retry", "Microsoft limitó temporalmente el envío.", null, 60, Throttled: true);
    outbox.CompleteBatch(batch.Select(i => i.Id).ToArray(), limited, t0.AddSeconds(26));
    var snap = outbox.Snapshot();
    Check(snap.All(i => i.Status == "retry" && i.Attempts == 0 && i.DelayedByLimit), "429: avisos en reintento, sin consumir intentos");
    Check(outbox.IsLimited(t0.AddSeconds(60)) && outbox.ClaimBatch(t0.AddSeconds(60)).Length == 0, "429: pausa general, no se envía nada durante la pausa");
    var now = t0.AddSeconds(26);
    for (var round = 0; round < 6; round++)
    {
        now = outbox.Limit.PausedUntilUtc!.Value.AddSeconds(1);
        var again = outbox.ClaimBatch(now);
        outbox.CompleteBatch(again.Select(i => i.Id).ToArray(), limited, now);
    }
    Check(outbox.Snapshot().All(i => i.Status == "retry"), "429: seis límites seguidos no marcan los avisos como fallidos");
    Check(outbox.Limit.ConsecutiveLimits == 7 && outbox.Limit.PausedUntilUtc - now >= TimeSpan.FromMinutes(60), "429: la pausa crece hasta 60 minutos");
    var restored = new NotificationOutbox(Fresh("group"));
    Check(restored.IsLimited(now.AddSeconds(5)), "429: la pausa se conserva tras reiniciar Vision");
    now = outbox.Limit.PausedUntilUtc!.Value.AddSeconds(1);
    var resumed = outbox.ClaimBatch(now);
    outbox.CompleteBatch(resumed.Select(i => i.Id).ToArray(), new EmailDelivery("accepted"), now);
    Check(resumed.Length == 10 && !outbox.IsLimited(now) && outbox.Limit.ConsecutiveLimits == 0, "429: al aceptar, se envían juntos y la pausa termina");
    Check(now - t0 > TimeSpan.FromMinutes(30) && outbox.Snapshot().All(i => i.Status == "accepted"), "429: avisos demorados por el límite no caducan a los 30 min");
}

// 4. Caducidad: sin límite caducan a 30 min; demorados por límite, a las 3 h.
{
    var outbox = new NotificationOutbox(Fresh("expiry"));
    outbox.Enqueue(Device("e1", "10.1.0.1", "Viejo", t0), "down");
    outbox.ClaimBatch(t0.AddMinutes(31));
    Check(outbox.Snapshot().Single().Status == "expired", "Caducidad: aviso normal caduca a los 30 min");
    var limitedBox = new NotificationOutbox(Fresh("expiry2"));
    limitedBox.Enqueue(Device("e2", "10.1.0.2", "Demorado", t0), "down");
    var claimed = limitedBox.ClaimBatch(t0.AddSeconds(25));
    limitedBox.CompleteBatch(claimed.Select(i => i.Id).ToArray(), new EmailDelivery("retry", "Límite", null, 60, Throttled: true), t0.AddSeconds(25));
    limitedBox.ClaimBatch(t0.AddHours(3).AddMinutes(1));
    Check(limitedBox.Snapshot().Single().Status == "expired", "Caducidad: demorado por límite caduca a las 3 h");
}

// 5. Prioridad: las alertas de red salen antes que los avisos al autor.
{
    var outbox = new NotificationOutbox(Fresh("priority"));
    outbox.EnqueueOwner("owner1", "mobile_login", "10.2.0.1", "Celular", t0, "Acceso", "Acceso", "autor@example.invalid");
    outbox.Enqueue(Device("p1", "10.2.0.2", "Switch", t0.AddSeconds(1)), "down");
    var first = outbox.ClaimBatch(t0.AddSeconds(30));
    var second = outbox.ClaimBatch(t0.AddSeconds(31));
    Check(first.Length == 1 && !first[0].OwnerOnly && second.Length == 1 && second[0].Id == "owner1", "Prioridad: red primero, aviso al autor después");
}

// 6. Equipo intermitente: tras la segunda caída en 30 min, sus cambios esperan un resumen.
{
    var outbox = new NotificationOutbox(Fresh("flap"));
    void Accept(DateTimeOffset at) { var b = outbox.ClaimBatch(at); outbox.CompleteBatch(b.Select(i => i.Id).ToArray(), new EmailDelivery("accepted"), at); }
    outbox.Enqueue(Device("f1", "10.3.0.1", "Oscila", t0), "down"); Accept(t0.AddSeconds(25));
    outbox.Enqueue(Device("f1", "10.3.0.1", "Oscila", t0, t0.AddMinutes(1)), "recovery"); Accept(t0.AddMinutes(1).AddSeconds(25));
    outbox.Enqueue(Device("f2", "10.3.0.1", "Oscila", t0.AddMinutes(3)), "down");
    outbox.Enqueue(Device("f2", "10.3.0.1", "Oscila", t0.AddMinutes(3), t0.AddMinutes(4)), "recovery");
    outbox.Enqueue(Device("f3", "10.3.0.1", "Oscila", t0.AddMinutes(5)), "down");
    outbox.Enqueue(Device("f3", "10.3.0.1", "Oscila", t0.AddMinutes(5), t0.AddMinutes(6)), "recovery");
    Check(outbox.ClaimBatch(t0.AddMinutes(10)).Length == 0, "Intermitente: no envía cada caída y recuperación");
    var summary = outbox.ClaimBatch(t0.AddMinutes(16).AddSeconds(30));
    Check(summary.Length == 4, $"Intermitente: un resumen al estabilizarse 10 min ({summary.Length} cambios)");
    var digest = AlertMessage.ComposeDigest(summary, outbox, t0.AddMinutes(16).AddSeconds(30));
    Check(digest.Message.Contains("Intermitente") && digest.Subject.Contains("1 equipo intermitente"), "Intermitente: el resumen lo identifica: " + digest.Subject);
    Check(digest.Message.Contains("todos los equipos incluidos respondían"), "Intermitente: indica el último estado");
}

// 7. Resumen seguro: nombres con HTML se muestran como texto.
{
    var outbox = new NotificationOutbox(Fresh("html"));
    outbox.Enqueue(Device("h1", "10.4.0.1", "<script>alert(1)</script>", t0), "down");
    outbox.Enqueue(Device("h2", "10.4.0.2", "Normal", t0), "down");
    var b = outbox.ClaimBatch(t0.AddSeconds(25));
    var digest = AlertMessage.ComposeDigest(b, outbox, t0.AddSeconds(25));
    Check(!digest.Message.Contains("<script>") && digest.Message.Contains("&lt;script&gt;"), "Resumen: nombres codificados como texto");
}

// 8. Worker: varios avisos producen un solo correo; un 429 pausa el envío.
{
    var outbox = new NotificationOutbox(Fresh("worker"));
    var past = DateTimeOffset.UtcNow.AddMinutes(-2);
    for (var i = 0; i < 3; i++) outbox.Enqueue(Device($"w{i}", $"10.5.0.{i}", $"Equipo {i}", past), "down");
    var transport = new FakeTransport(new EmailDelivery("accepted"));
    var worker = new EmailDeliveryWorker(outbox, transport, NullLogger<EmailDeliveryWorker>.Instance);
    await worker.StartAsync(CancellationToken.None);
    for (var i = 0; i < 100 && outbox.Snapshot().Any(n => n.Status != "accepted"); i++) await Task.Delay(20);
    await worker.StopAsync(CancellationToken.None);
    Check(transport.Sent.Count == 1 && transport.Sent[0].Kind == "digest" && outbox.Snapshot().All(n => n.Status == "accepted"), $"Worker: 3 avisos en un solo correo ({transport.Sent.Count} envíos)");

    var limitedOutbox = new NotificationOutbox(Fresh("worker429"));
    limitedOutbox.Enqueue(Device("l1", "10.6.0.1", "Equipo", past), "down");
    var limitedTransport = new FakeTransport(new EmailDelivery("retry", "Microsoft limitó temporalmente el envío.", null, 120, Throttled: true));
    var limitedWorker = new EmailDeliveryWorker(limitedOutbox, limitedTransport, NullLogger<EmailDeliveryWorker>.Instance);
    await limitedWorker.StartAsync(CancellationToken.None);
    for (var i = 0; i < 100 && !limitedOutbox.IsLimited(DateTimeOffset.UtcNow); i++) await Task.Delay(20);
    await Task.Delay(300);
    await limitedWorker.StopAsync(CancellationToken.None);
    Check(limitedTransport.Sent.Count == 1 && limitedOutbox.IsLimited(DateTimeOffset.UtcNow), "Worker: tras un 429 no insiste durante la pausa");
}

// 9. Avisos de acceso móvil: uno por IP y dispositivo cada 24 h, máximo 10 al día.
{
    var dir = Path.Combine(root, "owner"); Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "owner-notifications.json"), "{\"recipientAddress\":\"autor@example.invalid\",\"enabled\":true}");
    var outbox = new NotificationOutbox(Path.Combine(dir, "queue.json"));
    var owner = new OwnerNotifications(dir, outbox);
    var nowUtc = DateTimeOffset.UtcNow;
    var first = owner.MobileLogin("10.7.0.1", "Mozilla (Android)", nowUtc);
    var repeat = owner.MobileLogin("10.7.0.1", "Mozilla (Android)", nowUtc.AddMinutes(5));
    var otherDevice = owner.MobileLogin("10.7.0.1", "Mozilla (iPhone)", nowUtc.AddMinutes(6));
    Check(first && !repeat && otherDevice, "Acceso móvil: el mismo dispositivo e IP no repite aviso en 24 h");
    var queued = 0;
    for (var i = 2; i < 30; i++) if (owner.MobileLogin($"10.7.0.{i}", "Android", nowUtc.AddMinutes(10 + i))) queued++;
    Check(outbox.CountOwner("mobile_login", nowUtc.AddHours(-1)) == 10, $"Acceso móvil: máximo 10 avisos por día ({outbox.CountOwner("mobile_login", nowUtc.AddHours(-1))})");

    // 10. Configuración del autor dañada: no detiene el canal de correo.
    var broken = Path.Combine(root, "broken"); Directory.CreateDirectory(broken);
    File.WriteAllText(Path.Combine(broken, "owner-notifications.json"), "{ dañado");
    var ok = true; OwnerNotifications? damaged = null;
    try { damaged = new OwnerNotifications(broken, new NotificationOutbox(Path.Combine(broken, "q.json"))); } catch { ok = false; }
    Check(ok && damaged is { Enabled: false, ConfigurationError: not null }, "Autor: archivo dañado desactiva los avisos sin detener Vision");
}

// 11. Destinatarios y envío automático en la configuración de correo.
{
    var dir = Path.Combine(root, "mail"); Directory.CreateDirectory(dir);
    var settings = new EmailSettings("microsoft-graph", "organizational-device-code", Guid.NewGuid().ToString(),
        "sender@example.com", "test@example.com", "no-send", false, ["uno@example.com"]);
    File.WriteAllText(Path.Combine(dir, "email-settings.json"), JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Replace("}", ",\"campoExtra\":\"se conserva\"}"));
    using var channel = new EmailChannel(dir, new Lifetime());
    var rejected = false;
    try { await channel.UpdateRecipientsAsync(["uno@example.com", "usuario@empresa"], CancellationToken.None); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "Destinatarios: rechaza un dominio sin punto (usuario@empresa)");
    var saved = await channel.UpdateRecipientsAsync(["uno@example.com", "dos@empresa.com.mx"], CancellationToken.None);
    Check(saved.Length == 2, "Destinatarios: acepta dominios completos");
    await channel.SetAutomaticAlertsAsync(true, CancellationToken.None);
    var stored = File.ReadAllText(Path.Combine(dir, "email-settings.json"));
    Check(channel.AutomaticAlertsEnabled && stored.Contains("\"automaticAlertsEnabled\": true") && stored.Contains("se conserva") && stored.Contains("dos@empresa.com.mx"),
        "Interruptor: activa el envío y conserva los demás campos");
    await channel.SetAutomaticAlertsAsync(false, CancellationToken.None);
    Check(!channel.AutomaticAlertsEnabled, "Interruptor: desactiva el envío");
}

// 12. Invitaciones al portal móvil: validaciones antes de enviar; sin sesión de correo autorizada no sale nada.
{
    var dir = Path.Combine(root, "invite"); Directory.CreateDirectory(dir);
    File.WriteAllBytes(Path.Combine(dir, "mobile-access.dpapi"), System.Security.Cryptography.ProtectedData.Protect(
        System.Text.Encoding.UTF8.GetBytes("ABC123DEF456"), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
    var localIp = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
        .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && a.ToString() is var s &&
            (s.StartsWith("10.") || s.StartsWith("192.168.") || (s.StartsWith("172.") && int.Parse(s.Split('.')[1]) is >= 16 and <= 31)));
    void Vpn(string address) => File.WriteAllText(Path.Combine(dir, "mobile-vpn-settings.json"), $"{{\"vpnAddress\":\"{address}\",\"allowedSubnet\":\"10.250.0.0/24\"}}");
    File.WriteAllText(Path.Combine(dir, "email-settings.json"), JsonSerializer.Serialize(new EmailSettings("microsoft-graph", "organizational-device-code",
        Guid.NewGuid().ToString(), "sender@example.com", "test@example.com", "no-send", true, ["uno@example.com"]), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    var events = new EventRepository(dir);
    var outbox = new NotificationOutbox(Path.Combine(dir, "alertas-pendientes.json"), events);
    var mobile = new MobileAccess(dir, events);
    using var channel = new EmailChannel(dir, new Lifetime());
    int Status(Microsoft.AspNetCore.Http.IResult result) => (result as Microsoft.AspNetCore.Http.IStatusCodeHttpResult)?.StatusCode ?? 0;
    Vpn("10.250.0.1");
    var offline = new MobileInvitations(dir, mobile, new VpnMobileNetwork(dir), channel, outbox, events);
    Check(Status(await offline.SendAsync(new("usuario@empresa", 2), CancellationToken.None)) == 400, "Invitación: rechaza un correo sin dominio completo");
    Check(Status(await offline.SendAsync(new("persona@empresa.com", 9), CancellationToken.None)) == 400, "Invitación: rechaza un código inexistente");
    var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
    context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback; context.Request.Headers.UserAgent = "Android";
    mobile.Login(context, new MobileLogin(mobile.CodeForSlot(1)));
    Check(mobile.SlotInUse(1) && Status(await offline.SendAsync(new("persona@empresa.com", 1), CancellationToken.None)) == 409, "Invitación: no envía un código en uso");
    Check(Status(await offline.SendAsync(new("persona@empresa.com", 2), CancellationToken.None)) == 409, "Invitación: no envía si Vision no escucha en la VPN");
    if (localIp is not null)
    {
        Vpn(localIp.ToString());
        var online = new MobileInvitations(dir, mobile, new VpnMobileNetwork(dir), channel, outbox, events);
        outbox.CompleteBatch([], new EmailDelivery("retry", "Límite", null, 120, Throttled: true), DateTimeOffset.UtcNow);
        Check(Status(await online.SendAsync(new("persona@empresa.com", 2), CancellationToken.None)) == 503, "Invitación: respeta la pausa por límite de Microsoft");
        var other = Path.Combine(dir, "q2"); Directory.CreateDirectory(other);
        var unauthorized = new MobileInvitations(dir, mobile, new VpnMobileNetwork(dir), channel, new NotificationOutbox(Path.Combine(other, "alertas-pendientes.json")), events);
        Check(Status(await unauthorized.SendAsync(new("persona@empresa.com", 2), CancellationToken.None)) == 503, "Invitación: sin buzón autorizado no envía y lo informa");
    }
    else Console.WriteLine("OMITIDA Invitación: este equipo no tiene IPv4 privada para simular la VPN");
    Check(offline.Snapshot().Length == 0, "Invitación: no registra invitaciones que no salieron");
    var html = MobileInvitations.Compose("AB<12", "http://10.0.0.1:5081/");
    Check(html.Contains("AB&lt;12") && !html.Contains("AB<12") && html.Contains("http://10.0.0.1:5081/") && html.Contains("un solo dispositivo"),
        "Invitación: el correo incluye código, enlace e instrucciones, codificados como texto");
    Check(EmailChannel.IsValidAddress("persona@empresa.com.mx") && !EmailChannel.IsValidAddress("Persona <persona@empresa.com>") &&
        !EmailChannel.IsValidAddress("persona@empresa"), "Invitación: validación de correo compartida con destinatarios");
}

// 13. Redes autorizadas para la consulta móvil: varias subredes (VPN y VLAN) y el formato anterior de una sola.
{
    var dir = Path.Combine(root, "redes"); Directory.CreateDirectory(dir);
    VpnMobileNetwork Network(string json) { File.WriteAllText(Path.Combine(dir, "mobile-vpn-settings.json"), json); return new VpnMobileNetwork(dir); }
    bool Rejected(string json) { try { Network(json); return false; } catch (InvalidDataException) { return true; } }
    System.Net.IPAddress ip(string value) => System.Net.IPAddress.Parse(value);
    var many = Network("{\"vpnAddress\":\"10.250.0.1\",\"allowedSubnets\":[\"10.20.30.0/24\",\"192.168.50.0/24\"]}");
    Check(many.Allows(ip("10.20.30.7")) && many.Allows(ip("192.168.50.28")) && many.AllowedSubnets.Count == 2,
        "Redes: acepta clientes de la VPN y de la VLAN configuradas");
    Check(!many.Allows(ip("192.168.51.28")) && !many.Allows(ip("10.20.31.7")) && !many.Allows(ip("8.8.8.8")),
        "Redes: rechaza clientes fuera de las redes autorizadas");
    Check(many.Allows(ip("192.168.50.28").MapToIPv6()) && many.Allows(System.Net.IPAddress.Loopback), "Redes: IPv4 dentro de IPv6 y equipo local");
    var legacy = Network("{\"vpnAddress\":\"10.250.0.1\",\"allowedSubnet\":\"10.20.30.0/24\"}");
    Check(legacy.Allows(ip("10.20.30.9")) && !legacy.Allows(ip("192.168.50.28")) && legacy.AllowedSubnets.Count == 1,
        "Redes: el formato anterior de una sola red sigue funcionando");
    Check(Rejected("{\"vpnAddress\":\"10.250.0.1\",\"allowedSubnets\":[\"10.20.30.0/24\",\"8.8.8.0/24\"]}") &&
        Rejected("{\"vpnAddress\":\"10.250.0.1\",\"allowedSubnets\":[\"10.0.0.0/8\"]}") &&
        Rejected("{\"vpnAddress\":\"10.250.0.1\",\"allowedSubnets\":[]}") &&
        Rejected("{\"vpnAddress\":\"10.250.0.1\",\"allowedSubnets\":[\"texto\"]}"),
        "Redes: rechaza subredes públicas, demasiado amplias, vacías o inválidas");
}

Console.WriteLine(failures == 0 ? "\nTodas las pruebas pasaron. Sin correos reales." : $"\n{failures} pruebas fallaron.");
return failures == 0 ? 0 : 1;

sealed class FakeTransport(EmailDelivery result) : IAlertTransport
{
    public bool AutomaticAlertsEnabled => true;
    public List<PendingNotification> Sent { get; } = [];
    public Task<EmailDelivery> SendAlertAsync(PendingNotification notification, CancellationToken token)
    { Sent.Add(notification); return Task.FromResult(result); }
}
sealed class Lifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public void StopApplication() { }
}
