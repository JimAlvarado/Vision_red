using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, WriteIndented = true };
if (args.Contains("--self-test"))
{
    StateTests.Run();
    return;
}
var configPath = Path.Combine(AppContext.BaseDirectory, "inventario.json");
var config = JsonSerializer.Deserialize<Config>(await File.ReadAllTextAsync(configPath), json)
    ?? throw new InvalidDataException("Inventario vacío.");
config.Validate();
if (args.Contains("--validate"))
{
    Console.WriteLine($"Inventario válido: {config.Devices.Length} equipos; {config.Devices.Count(d => d.Critical)} críticos; IP únicas.");
    return;
}
var dataDir = Path.Combine(AppContext.BaseDirectory, "datos");
Directory.CreateDirectory(dataDir);
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
var states = config.Devices.ToDictionary(d => d.Ip, _ => new DeviceState());
using var limit = new SemaphoreSlim(config.MaxConcurrency);
Console.WriteLine("Monitor ICMP iniciado. Ctrl+C para detener. Sin envío de notificaciones en esta versión.");
try
{
    do
    {
        var cycle = Stopwatch.StartNew();
        var observations = await Task.WhenAll(config.Devices.Select(async device =>
        {
            await limit.WaitAsync(stop.Token);
            try
            {
                using var ping = new Ping();
                try
                {
                    var reply = await ping.SendPingAsync(IPAddress.Parse(device.Ip),
                        TimeSpan.FromMilliseconds(config.TimeoutMs), new byte[32], null, stop.Token);
                    return new Observation(device, DateTimeOffset.UtcNow, reply.Status == IPStatus.Success,
                        reply.Status.ToString(), reply.Status == IPStatus.Success ? reply.RoundtripTime : null);
                }
                catch (Exception ex) when (ex is PingException or System.Net.Sockets.SocketException or UnauthorizedAccessException)
                {
                    // An error in the local probe is not proof that a device is down.
                    return new Observation(device, DateTimeOffset.UtcNow, null, ex.Message, null);
                }
            }
            finally { limit.Release(); }
        }));
        var snapshot = new List<object>();
        foreach (var obs in observations)
        {
            var state = states[obs.Device.Ip];
            var transition = state.Apply(obs.Success, config.FailureThreshold, config.RecoveryThreshold);
            var record = new { timeUtc = obs.Time, ip = obs.Device.Ip, name = obs.Device.Name,
                critical = obs.Device.Critical, state = state.Status, incidentOpen = state.IncidentOpen,
                detail = obs.Detail, latencyMs = obs.LatencyMs, transition };
            snapshot.Add(record);
            Console.WriteLine($"{obs.Time.ToLocalTime().ToString("hh:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture)} {obs.Device.Ip,-16} {state.Status,-22} {obs.Device.Name} ({obs.Detail})");
            if (transition is not null)
            {
                var logPath = Path.Combine(dataDir, $"eventos-{obs.Time:yyyy-MM-dd}.jsonl");
                await File.AppendAllTextAsync(logPath, JsonSerializer.Serialize(record) + Environment.NewLine, stop.Token);
            }
        }
        var temporary = Path.Combine(dataDir, "estado.tmp");
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new
        {
            checkedAtUtc = DateTimeOffset.UtcNow, expectedIntervalSeconds = config.IntervalSeconds, devices = snapshot
        }, json), stop.Token);
        File.Move(temporary, Path.Combine(dataDir, "estado.json"), overwrite: true);
        if (args.Contains("--once")) break;
        var wait = TimeSpan.FromSeconds(config.IntervalSeconds) - cycle.Elapsed;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, stop.Token);
    } while (!stop.IsCancellationRequested);
}
catch (OperationCanceledException) when (stop.IsCancellationRequested)
{
    Console.WriteLine("Monitor detenido.");
}

record Device(string Ip, string Name, bool Critical, string Probe);
record Observation(Device Device, DateTimeOffset Time, bool? Success, string Detail, long? LatencyMs);
record Config(int IntervalSeconds, int TimeoutMs, int MaxConcurrency, int FailureThreshold,
    int RecoveryThreshold, Device[] Devices)
{
    public void Validate()
    {
        if (IntervalSeconds < 1 || TimeoutMs < 1 || MaxConcurrency < 1 || FailureThreshold < 1 || RecoveryThreshold < 1)
            throw new InvalidDataException("Los intervalos, límites y umbrales deben ser positivos.");
        if (Devices is null || Devices.Length == 0 || Devices.Any(d =>
            !IPAddress.TryParse(d.Ip, out _) || string.IsNullOrWhiteSpace(d.Name) || d.Probe != "ICMP"))
            throw new InvalidDataException("Equipo inválido. Esta versión admite únicamente ICMP.");
        if (Devices.Select(d => IPAddress.Parse(d.Ip).ToString()).Distinct().Count() != Devices.Length)
            throw new InvalidDataException("Hay IP duplicadas.");
    }
}

class DeviceState
{
    public string Status { get; private set; } = "Desconocido";
    public bool IncidentOpen { get; private set; }
    private int failures;
    private int successes;
    public string? Apply(bool? success, int failureThreshold, int recoveryThreshold)
    {
        var previous = Status;
        if (success is null)
        {
            failures = successes = 0;
            Status = "Error de comprobación";
        }
        else if (success.Value)
        {
            failures = 0;
            successes = Math.Min(successes + 1, recoveryThreshold);
            if (successes >= recoveryThreshold)
            {
                Status = "Disponible";
                if (IncidentOpen) { IncidentOpen = false; return "Recuperación"; }
            }
            else Status = "Confirmando respuesta";
        }
        else
        {
            successes = 0;
            failures = Math.Min(failures + 1, failureThreshold);
            if (IncidentOpen || failures >= failureThreshold)
            {
                Status = "No accesible";
                if (!IncidentOpen) { IncidentOpen = true; return "Caída confirmada"; }
            }
            else Status = "Sospechoso";
        }
        return previous == Status ? null : "Cambio de estado";
    }
}

static class StateTests
{
    public static void Run()
    {
        void Check(bool ok, string name) { if (!ok) throw new Exception($"Falló: {name}"); }
        var s = new DeviceState();
        s.Apply(false, 3, 2); s.Apply(false, 3, 2);
        Check(!s.IncidentOpen, "sin caída prematura");
        Check(s.Apply(false, 3, 2) == "Caída confirmada", "tercer fallo abre incidente");
        Check(s.Apply(false, 3, 2) is null, "sin incidentes duplicados");
        s.Apply(true, 3, 2);
        Check(s.IncidentOpen, "una respuesta no cierra incidente");
        Check(s.Apply(true, 3, 2) == "Recuperación" && !s.IncidentOpen, "recuperación confirmada");
        var e = new DeviceState();
        e.Apply(false, 3, 2); e.Apply(false, 3, 2); e.Apply(null, 3, 2); e.Apply(false, 3, 2);
        Check(!e.IncidentOpen, "error local interrumpe fallos consecutivos");
        e.Apply(false, 3, 2); e.Apply(false, 3, 2); e.Apply(null, 3, 2);
        Check(e.IncidentOpen && e.Status == "Error de comprobación", "error no cierra un incidente existente");
        var f = new DeviceState();
        for (var i = 0; i < 10; i++) { f.Apply(false, 3, 2); f.Apply(true, 3, 2); }
        Check(!f.IncidentOpen, "fallos alternados no confirman caída");
        Console.WriteLine("8 pruebas de estados completadas correctamente.");
    }
}
