using System.Runtime.InteropServices;
using System.Text;

// This runs in the signed-in Windows session, separately from browser autoplay.
public sealed class LocalSoundWorker(IncidentRepository incidents, EventRepository events,
    ILogger<LocalSoundWorker> logger) : BackgroundService
{
    private readonly object soundGate = new();
    private readonly Dictionary<string, bool> seen = incidents.Snapshot().ToDictionary(i => i.Id, i => i.ClosedAtUtc is not null);
    private readonly HashSet<string> completed = incidents.Snapshot().SelectMany(i => i.ClosedAtUtc is null ? new[]{"down:"+i.Id} : new[]{"down:"+i.Id,"recovery:"+i.Id}).ToHashSet();
    private readonly Dictionary<string,(Incident Incident,string Kind,DateTimeOffset Due)> pending = [];
    public bool Available => OperatingSystem.IsWindows() && Environment.UserInteractive && System.Diagnostics.Process.GetCurrentProcess().SessionId != 0;
    public bool? LastPlaybackSucceeded { get; private set; }
    public DateTimeOffset? LastPlaybackAtUtc { get; private set; }

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(byte[] sound, IntPtr module, uint flags);

    public static byte[] Tone(int frequency)
    {
        const int rate = 22050, count = 11025;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
        writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
        for (var i = 0; i < count; i++)
        {
            var t = (double)i / rate;
            var envelope = Math.Min(1, t / .02) * Math.Min(1, (.5 - t) / .07);
            writer.Write((short)(16000 * envelope * Math.Sin(2 * Math.PI * frequency * t)));
        }
        return stream.ToArray();
    }

    public bool Emit(int frequency)
    {
        lock (soundGate)
        {
            // SND_MEMORY | SND_NODEFAULT | SND_SYNC: one tone; buffer lives until playback ends.
            var ok = Available && PlaySound(Tone(frequency), IntPtr.Zero, 0x0004 | 0x0002);
            LastPlaybackAtUtc = DateTimeOffset.UtcNow; LastPlaybackSucceeded = ok;
            return ok;
        }
    }

    public bool? Announce(string id,string kind)
    {
        if(kind is not ("down" or "recovery"))return null;
        var incident=incidents.Snapshot().FirstOrDefault(i=>i.Id==id);
        if(incident is null || kind=="recovery" && incident.ClosedAtUtc is null)return null;
        return EmitOnce(incident,kind);
    }
    private bool EmitOnce(Incident incident,string kind)
    {
        lock(soundGate)
        {
            var key=kind+":"+incident.Id;
            if(completed.Contains(key))return true;
            var ok=Emit(880);
            if(ok)completed.Add(key);
            events.Add($"sound:{incident.Id}:{kind}",DateTimeOffset.UtcNow,"system",ok?"info":"warning",ok?"sound_playback_completed":"sound_playback_failed",
                incident.Ip,incident.Name,reference:incident.Id,description:ok?"Windows completó un pitido asociado al aviso visual. No confirma audición física.":"Windows no pudo reproducir el pitido; revisar la salida de audio.");
            return ok;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Available) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var incident in incidents.Snapshot())
                {
                    var closed = incident.ClosedAtUtc is not null;
                    var fresh = !seen.TryGetValue(incident.Id, out var previouslyClosed) || closed && !previouslyClosed;
                    seen[incident.Id] = closed;
                    if (!fresh) continue;
                    var kind=closed?"recovery":"down";
                    // Give the visible panel its five-second polling cycle to paint the alert first.
                    pending[kind+":"+incident.Id]=(incident,kind,DateTimeOffset.UtcNow.AddSeconds(7));
                }
                foreach(var item in pending.Where(p=>p.Value.Due<=DateTimeOffset.UtcNow).ToArray())
                { EmitOnce(item.Value.Incident,item.Value.Kind);pending.Remove(item.Key); }
            }
            catch (Exception error) { logger.LogError(error, "No se pudo reproducir el aviso local de Vision."); }
            try { await Task.Delay(1000, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
