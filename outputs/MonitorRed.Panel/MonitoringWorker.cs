using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

public sealed record DeviceReading(string Ip, string Name, string Status, long? LatencyMs, string Detail);
public sealed record MonitorSnapshot(DateTimeOffset? CheckedAtUtc, string Condition, DeviceReading[] Devices,
    bool IncidentDetectionEnabled = false);
public sealed class MonitorState
{
    private MonitorSnapshot _snapshot = new(null, "starting", []);
    public MonitorSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public void Publish(MonitorSnapshot snapshot) => Volatile.Write(ref _snapshot, snapshot);
}

public sealed class MonitoringWorker(string topologyPath, string dataDir, MonitorState state, IncidentRepository incidents,
    NotificationOutbox outbox, ILogger<MonitoringWorker> logger) : BackgroundService
{
    private readonly Dictionary<string, ProbeState> states = new();
    private readonly ProbeState networkState = new();
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private bool IncidentDetectionEnabled()
    {
        var settingsPath = Path.Combine(dataDir, "monitor-settings.json");
        try
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(settingsPath));
            return settings.RootElement.TryGetProperty("incidentDetectionEnabled", out var enabled) &&
                enabled.ValueKind == JsonValueKind.True;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "No se pudo leer la configuración del monitor. Las alertas quedan desactivadas.");
            return false;
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var elapsed = Stopwatch.StartNew();
            try { await CheckAll(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falló la ronda de comprobaciones.");
                state.Publish(new(DateTimeOffset.UtcNow, "probe-error", []));
            }
            var wait = TimeSpan.FromSeconds(10) - elapsed.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                try { await Task.Delay(wait, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
    private async Task CheckAll(CancellationToken token)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(topologyPath, token));
        var devices = doc.RootElement.GetProperty("devices").EnumerateArray().Select(d => new Target(
            d.GetProperty("ip").GetString()!, d.GetProperty("name").GetString()!,
            d.TryGetProperty("critical", out var critical) && critical.GetBoolean())).ToArray();
        var criticalIps = devices.Where(d => d.Critical).Select(d => d.Ip).ToHashSet();
        foreach (var incident in incidents.Snapshot().Where(i => criticalIps.Contains(i.Ip) || i.Ip == AlertMessage.AccessIp))
        {
            outbox.Enqueue(incident, "down");
            if (incident.ClosedAtUtc is not null) outbox.Enqueue(incident, "recovery");
        }
        var active = devices.Select(d => d.Ip).ToHashSet();
        foreach (var old in states.Keys.Where(k => !active.Contains(k)).ToArray()) states.Remove(old);
        using var limit = new SemaphoreSlim(8);
        var observations = await Task.WhenAll(devices.Select(async d =>
        {
            await limit.WaitAsync(token);
            try
            {
                using var ping = new Ping();
                try
                {
                    var reply = await ping.SendPingAsync(IPAddress.Parse(d.Ip), TimeSpan.FromMilliseconds(1500),
                        new byte[32], null, token);
                    return new Observation(d, reply.Status == IPStatus.Success,
                        reply.Status.ToString(), reply.Status == IPStatus.Success ? reply.RoundtripTime : null);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is PingException or System.Net.Sockets.SocketException or UnauthorizedAccessException)
                { return new Observation(d, null, ex.GetType().Name, null); }
            }
            finally { limit.Release(); }
        }));
        var reachable = observations.Count(o => o.Success == true);
        var incidentDetectionEnabled = IncidentDetectionEnabled();
        string[] referenceIps = [];
        try
        {
            using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDir, "monitor-settings.json")));
            if (settings.RootElement.TryGetProperty("networkReferenceIps", out var references))
                referenceIps = references.EnumerateArray().Select(r => r.GetString()!).ToArray();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        var access = referenceIps.Length == 0 ? reachable > 0 : observations.Any(o => referenceIps.Contains(o.Target.Ip) && o.Success == true);
        var condition = devices.Length == 0 ? "empty" : access ? "operating" :
            observations.All(o => o.Success is null) ? "probe-error" : "no-reachability";
        var readings = new List<DeviceReading>(observations.Length);
        var now = DateTimeOffset.UtcNow;
        if (devices.Length > 0 && condition != "probe-error")
        {
            var accessStatus = networkState.Apply(access);
            if (incidentDetectionEnabled && (accessStatus == "online" || networkState.HasBeenOnline))
            {
                var networkEvent = incidents.Update(AlertMessage.AccessIp, "Acceso a la red monitoreada", accessStatus,
                    access ? "Referencias de red responden" : "Referencias de red sin respuesta; revisar VPN y rutas", now);
                if (networkEvent is not null) outbox.Enqueue(networkEvent.Incident, networkEvent.Kind);
            }
        }
        foreach (var obs in observations)
        {
            if (!states.TryGetValue(obs.Target.Ip, out var current))
                states[obs.Target.Ip] = current = new ProbeState();
            var next = condition == "no-reachability" ? current.Unreachable() :
                current.Apply(obs.Success);
            readings.Add(new(obs.Target.Ip, obs.Target.Name, next, obs.LatencyMs, obs.Detail));
            if (condition == "operating" && incidentDetectionEnabled && (next == "online" || current.HasBeenOnline))
            {
                var transition = incidents.Update(obs.Target.Ip, obs.Target.Name, next, obs.Detail, now);
                if (transition is not null && obs.Target.Critical)
                    outbox.Enqueue(transition.Incident, transition.Kind);
            }
            if (next != current.LastPublished)
            {
                var eventData = new { timeUtc = now, ip = obs.Target.Ip, name = obs.Target.Name,
                    from = current.LastPublished, to = next, detail = obs.Detail };
                var logPath = Path.Combine(dataDir, $"eventos-monitor-{now:yyyy-MM-dd}.jsonl");
                await File.AppendAllTextAsync(logPath, JsonSerializer.Serialize(eventData) + Environment.NewLine, token);
                current.LastPublished = next;
            }
        }
        state.Publish(new(now, condition, readings.ToArray(), incidentDetectionEnabled));
    }
    private sealed record Target(string Ip, string Name, bool Critical);
    private sealed record Observation(Target Target, bool? Success, string Detail, long? LatencyMs);
}

