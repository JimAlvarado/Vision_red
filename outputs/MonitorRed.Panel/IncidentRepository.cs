using System.Text.Json;

public sealed record Incident(string Id, string Ip, string Name, DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ClosedAtUtc, string Detail);
public sealed record IncidentTransition(Incident Incident, string Kind);

public sealed class IncidentRepository
{
    private readonly object gate = new();
    private readonly string path;
    private readonly List<Incident> incidents;
    private readonly EventRepository? events;
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public IncidentRepository(string path, EventRepository? events = null)
    {
        this.path = path;
        this.events = events;
        incidents = File.Exists(path)
            ? JsonSerializer.Deserialize<List<Incident>>(File.ReadAllText(path), json) ?? []
            : [];
    }
    public Incident[] Snapshot()
    {
        lock (gate) return incidents.OrderByDescending(i => i.OpenedAtUtc).ToArray();
    }
    public IncidentTransition? Update(string ip, string name, string status, string detail, DateTimeOffset now)
    {
        if (status is not ("offline" or "online")) return null;
        lock (gate)
        {
            var active = incidents.FirstOrDefault(i => i.Ip == ip && i.ClosedAtUtc is null);
            IncidentTransition transition;
            if (status == "offline" && active is null)
            {
                var opened = new Incident(Guid.NewGuid().ToString("N"), ip, name, now, null, detail);
                incidents.Add(opened);
                transition = new(opened, "down");
            }
            else if (status == "online" && active is not null)
            {
                var index = incidents.IndexOf(active);
                incidents[index] = active with { ClosedAtUtc = now, Detail = detail };
                transition = new(incidents[index], "recovery");
            }
            else return null;
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(incidents, json));
            File.Move(temp, path, true);
            events?.Incident(transition.Incident);
            return transition;
        }
    }
}
