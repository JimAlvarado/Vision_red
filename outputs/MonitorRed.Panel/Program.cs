using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args=args,
    ContentRootPath=WindowsServiceHelpers.IsWindowsService()?AppContext.BaseDirectory:Directory.GetCurrentDirectory() });
builder.Services.AddWindowsService(options=>options.ServiceName="Vision");
var monitorDataDir = Path.Combine(builder.Environment.ContentRootPath, "datos");
Directory.CreateDirectory(monitorDataDir);
var mobileNetwork = new VpnMobileNetwork(monitorDataDir);
builder.WebHost.UseUrls(mobileNetwork.ListeningOnVpn
    ? ["http://127.0.0.1:5080", "http://127.0.0.1:5081", mobileNetwork.Url]
    : ["http://127.0.0.1:5080", "http://127.0.0.1:5081"]);
builder.Services.AddSingleton(mobileNetwork);
var monitorTopologyPath = Path.Combine(monitorDataDir, "topologia.json");
var monitorSettingsPath = Path.Combine(monitorDataDir, "monitor-settings.json");
if (!File.Exists(monitorSettingsPath))
    File.WriteAllText(monitorSettingsPath, "{\"incidentDetectionEnabled\":false}");
builder.Services.AddSingleton<MonitorState>();
builder.Services.AddSingleton(new EventRepository(monitorDataDir));
builder.Services.AddSingleton(provider => new MobileAccess(monitorDataDir,provider.GetRequiredService<EventRepository>(),provider.GetRequiredService<OwnerNotifications>()));
builder.Services.AddSingleton(provider => new MobileInvitations(monitorDataDir, provider.GetRequiredService<MobileAccess>(),
    provider.GetRequiredService<VpnMobileNetwork>(), provider.GetRequiredService<EmailChannel>(),
    provider.GetRequiredService<NotificationOutbox>(), provider.GetRequiredService<EventRepository>()));
builder.Services.AddSingleton(provider => new OwnerNotifications(monitorDataDir, provider.GetRequiredService<NotificationOutbox>()));
builder.Services.AddSingleton(provider => new EmailChannel(monitorDataDir,
    provider.GetRequiredService<IHostApplicationLifetime>(), provider.GetRequiredService<OwnerNotifications>()));
builder.Services.AddSingleton<IAlertTransport>(provider => provider.GetRequiredService<EmailChannel>());
builder.Services.AddHostedService<EmailDeliveryWorker>();
builder.Services.AddSingleton(provider => new IncidentRepository(Path.Combine(monitorDataDir, "incidentes.json"),provider.GetRequiredService<EventRepository>()));
builder.Services.AddSingleton(provider => new NotificationOutbox(Path.Combine(monitorDataDir, "alertas-pendientes.json"),provider.GetRequiredService<EventRepository>()));
builder.Services.AddHostedService<EventJournalWorker>();
builder.Services.AddSingleton<LocalSoundWorker>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<LocalSoundWorker>());
builder.Services.AddHostedService(provider => new MonitoringWorker(monitorTopologyPath,
    monitorDataDir, provider.GetRequiredService<MonitorState>(), provider.GetRequiredService<IncidentRepository>(),
    provider.GetRequiredService<NotificationOutbox>(),
    provider.GetRequiredService<ILogger<MonitoringWorker>>()));
var app = builder.Build();
var gate = new SemaphoreSlim(1);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var dataDir = Path.Combine(app.Environment.ContentRootPath, "datos");
Directory.CreateDirectory(dataDir);
var topologyPath = Path.Combine(dataDir, "topologia.json");
if (!File.Exists(topologyPath))
{
    var inventoryPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "MonitorRed", "inventario.json"));
    using var inventory = JsonDocument.Parse(File.ReadAllText(inventoryPath));
    var devices = inventory.RootElement.GetProperty("devices").EnumerateArray().Select((d, i) => new Device(
        Guid.NewGuid().ToString("N"), d.GetProperty("name").GetString()!, d.GetProperty("ip").GetString()!,
        "switch", true, 90 + i % 4 * 300, 85 + i / 4 * 165, d.GetProperty("note").GetString() ?? "")).ToArray();
    File.WriteAllText(topologyPath, JsonSerializer.Serialize(new Topology(devices, [], 0), json));
}
app.Use(async (context, next) =>
{
    if (context.Connection.LocalPort == 5081)
    {
        if (!mobileNetwork.Allows(context.Connection.RemoteIpAddress)) { context.Response.StatusCode = 403; return; }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; connect-src 'self'; img-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        var path = context.Request.Path.Value ?? "/";
        var publicAsset = path is "/" or "/mobile.html" or "/mobile.css" or "/mobile.js" or "/time-format.js" or "/events.css" or "/events.js" or "/branding.css" or "/layout.css" or "/alarm.css" or "/alarm.js" or "/branding.js" or "/particles.js" or "/vision-logo.svg" or "/favicon.ico";
        var login = path == "/api/mobile/login" && context.Request.Method == "POST";
        var logout = path == "/api/mobile/logout" && context.Request.Method == "POST";
        var read = context.Request.Method == "GET" && path is "/api/status" or "/api/incidents" or "/api/topology" or "/api/mobile/overview" or "/api/display-settings" or "/api/events";
        if (!publicAsset && !login && !logout && !read) { context.Response.StatusCode = 403; return; }
        if (publicAsset && context.Request.Method is not ("GET" or "HEAD")) { context.Response.StatusCode = 403; return; }
        if (login || logout)
        {
            if (context.Request.Headers["X-Vision-Mobile"] != "1") { context.Response.StatusCode = 403; return; }
            if (context.Request.Headers.Origin.ToString() is { Length: > 0 } origin &&
                (!Uri.TryCreate(origin, UriKind.Absolute, out var parsed) || parsed.Authority != context.Request.Host.Value))
            { context.Response.StatusCode = 403; return; }
            if ((context.Request.ContentLength ?? 0) > 1024) { context.Response.StatusCode = 413; return; }
        }
        if ((read || logout) && !context.RequestServices.GetRequiredService<MobileAccess>().IsAuthenticated(context))
        { context.Response.StatusCode = 401; return; }
        if (path == "/") context.Request.Path = "/mobile.html";
        await next();
        return;
    }
    // Local editor: reject cross-origin writes and unexpected Host headers.
    if (context.Request.Host.Host != "127.0.0.1" && context.Request.Host.Host != "localhost")
    { context.Response.StatusCode = 403; return; }
    if (context.Request.Method != "GET" && context.Request.Method != "HEAD")
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && origin != $"{context.Request.Scheme}://{context.Request.Host}")
        { context.Response.StatusCode = 403; return; }
        if (context.Request.Headers["X-Topology-Editor"] != "1")
        { context.Response.StatusCode = 403; return; }
        // Small configuration bodies: limit before model binding reads them, including chunked requests.
        if (context.Request.Path.StartsWithSegments("/api/email") || context.Request.Path.StartsWithSegments("/api/mobile"))
        {
            if (context.Request.ContentLength > 16384) { context.Response.StatusCode = 413; return; }
            if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodyLimit) bodyLimit.MaxRequestBodySize = 16384;
        }
    }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache" });
app.MapGet("/api/sound", (LocalSoundWorker sound) => Results.Ok(new { available = sound.Available, sound.LastPlaybackSucceeded, sound.LastPlaybackAtUtc }));
app.MapPost("/api/sound/announce", (SoundAnnouncement announcement, LocalSoundWorker sound) =>
{
    if(announcement.Id?.Length!=32)return Results.BadRequest();
    var played=sound.Announce(announcement.Id,announcement.Kind);
    return played is null?Results.BadRequest():Results.Ok(new { handled=played.Value });
});
app.MapPost("/api/sound/test", (LocalSoundWorker sound, EventRepository events) =>
{
    var ok = sound.Emit(880);
    events.Add("sound:test:"+Guid.NewGuid(), DateTimeOffset.UtcNow, "system", ok ? "info" : "warning", "sound_test",
        description: ok ? "Prueba de un pitido completada en Windows, sin incidente ni correo. Audición pendiente de confirmación." : "Windows no pudo reproducir el pitido de prueba.");
    return Results.Ok(new { played = ok });
});
app.MapGet("/api/events", (EventRepository events, HttpContext context, string? search, string? category, string? severity, DateTimeOffset? since, DateTimeOffset? until, int? page) =>
{
    context.Response.Headers.CacheControl="no-store";
    if(search?.Length>200)return Results.BadRequest();
    return Results.Ok(events.Query(search,category,severity,since,until,page??1));
});
var displaySettingsPath = Path.Combine(dataDir, "display-settings.json");
app.MapGet("/api/display-settings", async () =>
{
    await gate.WaitAsync();
    try { return Results.Ok(File.Exists(displaySettingsPath)
        ? JsonSerializer.Deserialize<DisplaySettings>(await File.ReadAllTextAsync(displaySettingsPath), json)
        : new DisplaySettings("Topología de red")); }
    finally { gate.Release(); }
});
app.MapPut("/api/display-settings", async (DisplaySettings settings) =>
{
    var title = settings.TopologyTitle?.Trim();
    if (string.IsNullOrWhiteSpace(title) || title.Length > 100 || title.Any(char.IsControl))
        return Results.BadRequest(new { error = "Escribe un título de 1 a 100 caracteres." });
    await gate.WaitAsync();
    try
    {
        var value = new DisplaySettings(title);
        var temporary = displaySettingsPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, json));
        File.Move(temporary, displaySettingsPath, true);
        app.Services.GetRequiredService<EventRepository>().Add(Guid.NewGuid().ToString(),DateTimeOffset.UtcNow,"configuration","info","title_updated",description:"Título de topología actualizado: "+title);
        return Results.Ok(value);
    }
    finally { gate.Release(); }
});
app.MapGet("/api/mobile/access", (MobileAccess mobile, MobileInvitations invitations, VpnMobileNetwork network, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(new { code = mobile.LocalAccessCode, codes = mobile.LocalAccessCodes, accesses = mobile.AccessStatus(), invitations = invitations.Snapshot(), url = network.Url, listeningOnVpn = network.ListeningOnVpn, mode = "private-vpn" });
});
app.MapPost("/api/mobile/access/{slot:int}/release", (int slot, MobileAccess mobile) => mobile.Release(slot));
app.MapPost("/api/mobile/invite", (MobileInvitationRequest input, MobileInvitations invitations, HttpContext context) =>
    invitations.SendAsync(input, context.RequestAborted));
app.MapPost("/api/mobile/login", (HttpContext context, MobileLogin input, MobileAccess mobile) => mobile.Login(context, input));
app.MapPost("/api/mobile/logout", (HttpContext context, MobileAccess mobile) => mobile.Logout(context));
app.MapGet("/api/mobile/overview", (NotificationOutbox outbox, EmailChannel email) =>
{
    var alerts = outbox.Snapshot();
    var limit = outbox.Limit;
    return Results.Ok(new { automaticAlertsEnabled = email.AutomaticAlertsEnabled,
        limitedUntilUtc = limit.PausedUntilUtc > DateTimeOffset.UtcNow ? limit.PausedUntilUtc : null,
        accepted = alerts.Count(i => i.Status == "accepted"), pending = alerts.Count(i => i.Status is "awaiting-configuration" or "retry" or "sending"),
        needsAttention = alerts.Count(i => i.Status is "failed" or "unknown" or "expired"),
        lastAcceptedAtUtc = alerts.Where(i => i.AcceptedAtUtc is not null).Select(i => i.AcceptedAtUtc).OrderDescending().FirstOrDefault() });
});
app.MapGet("/api/status", (MonitorState monitor) => Results.Ok(monitor.Snapshot));
app.MapGet("/api/incidents", (IncidentRepository incidents) => Results.Ok(incidents.Snapshot()));
app.MapGet("/api/notifications", (NotificationOutbox outbox) => Results.Ok(outbox.Snapshot()));
app.MapGet("/api/email/status", async (EmailChannel email, NotificationOutbox outbox, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(await email.StatusAsync(outbox.Limit));
});
app.MapPut("/api/email/automatic", async (EmailAutomaticRequest input, EmailChannel email, EventRepository events, HttpContext context) =>
{
    if (input.Enabled is not { } enabled) return Results.BadRequest(new { error = "Indica si el envío automático queda activado." });
    try { await email.SetAutomaticAlertsAsync(enabled, context.RequestAborted); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
    { return Results.Json(new { error = "No se pudo guardar el ajuste. Revisa los permisos y vuelve a intentar." }, statusCode: 503); }
    events.Add(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, "configuration", enabled ? "info" : "warning",
        enabled ? "email_automatic_enabled" : "email_automatic_disabled",
        description: enabled ? "Envío automático de alertas de red activado desde el editor local." : "Envío automático de alertas de red desactivado desde el editor local.");
    return Results.Ok(new { automaticAlertsEnabled = enabled });
});
app.MapPost("/api/email/check", async (EmailChannel email) => Results.Ok(new { ready = await email.CheckConnectionAsync() }));
app.MapPut("/api/email/recipients", async (EmailRecipientsRequest input, EmailChannel email, EventRepository events, HttpContext context) =>
{
    string[] recipients;
    try { recipients = await email.UpdateRecipientsAsync(input.Recipients, context.RequestAborted); }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    { return Results.Json(new { error = "No se pudieron guardar los destinatarios. Revisa los permisos y vuelve a intentar." }, statusCode: 503); }
    events.Add(Guid.NewGuid().ToString(), DateTimeOffset.UtcNow, "configuration", "info", "email_recipients_updated",
        description: $"Lista de destinatarios de correo actualizada: {recipients.Length} destinatarios.");
    return Results.Ok(new { recipients });
});
app.MapPost("/api/email/authorize", async (EmailChannel email, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(await email.StartAuthorizationAsync());
});
app.MapPost("/api/email/test", async (EmailChannel email) =>
{
    try { return Results.Ok(await email.SendTestAsync()); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = ex.Message }); }
});
app.MapGet("/api/topology", async () =>
{
    await gate.WaitAsync();
    try { return Results.Text(await File.ReadAllTextAsync(topologyPath), "application/json"); }
    finally { gate.Release(); }
});
app.MapPut("/api/topology", async (Topology model) =>
{
    var error = Validate(model);
    if (error is not null) return Results.BadRequest(new { error });
    await gate.WaitAsync();
    try
    {
        var current = JsonSerializer.Deserialize<Topology>(await File.ReadAllTextAsync(topologyPath), json)!;
        if (model.Revision != current.Revision) return Results.Conflict(new { error = "Hay cambios desde otra ventana. Recarga antes de editar." });
        model = model with { Revision = current.Revision + 1 };
        var tmp = topologyPath + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(model, json));
        File.Move(tmp, topologyPath, true);
        var eventLog=app.Services.GetRequiredService<EventRepository>();
        var changedAt=DateTimeOffset.UtcNow;
        foreach(var device in model.Devices.Where(d=>!current.Devices.Any(old=>old.Id==d.Id)))
            eventLog.Add($"topology:{model.Revision}:added:{device.Id}",changedAt,"configuration","info","device_added",device.Ip,device.Name,to:"registered",reference:device.Id,description:"Equipo agregado al monitoreo. Sus comprobaciones y cambios de estado se registran automáticamente.");
        foreach(var device in current.Devices.Where(d=>!model.Devices.Any(next=>next.Id==d.Id)))
            eventLog.Add($"topology:{model.Revision}:removed:{device.Id}",changedAt,"configuration","info","device_removed",device.Ip,device.Name,from:"registered",to:"removed",reference:device.Id,description:"Equipo retirado del monitoreo. Su historial anterior se conserva.");
        app.Services.GetRequiredService<EventRepository>().Add($"topology:{model.Revision}",DateTimeOffset.UtcNow,"configuration","info","topology_updated",reference:model.Revision.ToString(),description:$"Topología guardada: {model.Devices.Length} equipos y {model.Edges.Length} enlaces.");
        return Results.Ok(new { revision = model.Revision });
    }
    finally { gate.Release(); }
});
app.Lifetime.ApplicationStopping.Register(()=>app.Services.GetRequiredService<EventRepository>().Add(Guid.NewGuid().ToString(),DateTimeOffset.UtcNow,"system","info","monitor_stopping",description:"Se solicitó detener Vision."));
app.Run();

static string? Validate(Topology t)
{
    if (t.Devices is null || t.Edges is null || t.Devices.Length > 2000 || t.Edges.Length > 10000) return "Inventario fuera de límites.";
    var types = new[] { "switch", "camera", "server", "router", "printer", "other" };
    if (t.Devices.Any(d => string.IsNullOrWhiteSpace(d.Id) || d.Id.Length > 80 ||
        string.IsNullOrWhiteSpace(d.Name) || d.Name.Length > 100 || !IPAddress.TryParse(d.Ip, out _) ||
        !types.Contains(d.Type) || !double.IsFinite(d.X) || !double.IsFinite(d.Y) || Math.Abs(d.X) > 100000 || Math.Abs(d.Y) > 100000 || (d.Note?.Length ?? 0) > 1000)) return "Revisa nombre, IP, tipo y posición de cada equipo.";
    if (t.Devices.Select(d => d.Id).Distinct().Count() != t.Devices.Length ||
        t.Devices.Select(d => IPAddress.Parse(d.Ip).ToString()).Distinct().Count() != t.Devices.Length) return "Hay identificadores o IP duplicados.";
    var ids = t.Devices.Select(d => d.Id).ToHashSet();
    if (t.Edges.Any(e => !ids.Contains(e.Source) || !ids.Contains(e.Target) || e.Source == e.Target)) return "Conexión inválida.";
    if (t.Edges.Select(e => (e.Source, e.Target)).Distinct().Count() != t.Edges.Length) return "Conexión duplicada.";
    // Physical networks can contain redundant links and cycles; keep them editable.
    return null;
}
record Device(string Id, string Name, string Ip, string Type, bool Critical, double X, double Y, string Note);
record Edge(string Source, string Target);
record Topology(Device[] Devices, Edge[] Edges, long Revision);
record DisplaySettings(string TopologyTitle);
record SoundAnnouncement(string Id,string Kind);
record EmailRecipientsRequest(string[]? Recipients);
record EmailAutomaticRequest(bool? Enabled);


