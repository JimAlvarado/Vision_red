using System.Text.Json;

public sealed record PendingNotification(string Id, string IncidentId, string Ip, string Name,
    string Kind, string Channel, DateTimeOffset CreatedAtUtc, string Status, string Subject, string Message,
    int Attempts = 0, DateTimeOffset? NextAttemptAtUtc = null, DateTimeOffset? AcceptedAtUtc = null,
    string? LastError = null, string? ProviderRequestId = null);

public sealed class NotificationOutbox
{
    private readonly object gate = new();
    private readonly string path;
    private readonly List<PendingNotification> items;
    private readonly EventRepository? events;
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public NotificationOutbox(string path, EventRepository? events = null)
    {
        this.path = path;
        this.events = events;
        items = File.Exists(path)
            ? JsonSerializer.Deserialize<List<PendingNotification>>(File.ReadAllText(path), json) ?? []
            : [];
        if (items.Any(i => i.Status == "sending"))
        {
            for (var i = 0; i < items.Count; i++)
                if (items[i].Status == "sending") items[i] = items[i] with { Status = "unknown", LastError = "Vision se reinició durante el envío. Revisar Elementos enviados antes de repetir." };
            Save();
        }
    }

    public PendingNotification[] Snapshot()
    {
        lock (gate) return items.OrderByDescending(i => i.CreatedAtUtc).ToArray();
    }

    public void Enqueue(Incident incident, string kind)
    {
        if (kind is not ("down" or "recovery")) throw new ArgumentOutOfRangeException(nameof(kind));
        lock (gate)
        {
            if (items.Any(i => i.IncidentId == incident.Id && i.Kind == kind && i.Channel == "email")) return;
            var time = kind == "down" ? incident.OpenedAtUtc : incident.ClosedAtUtc ?? DateTimeOffset.UtcNow;
            var (subject, message) = AlertMessage.Compose(incident, kind);
            items.Add(new(Guid.NewGuid().ToString("N"), incident.Id, incident.Ip, incident.Name,
                kind, "email", time, "awaiting-configuration", subject, message));
            Save();
        }
    }

    public PendingNotification? Claim(DateTimeOffset now)
    {
        lock (gate)
        {
            // Events older than 30 minutes need review, rather than late operational alerts.
            var changed = false;
            for (var i = 0; i < items.Count; i++)
                if (items[i].Status is "awaiting-configuration" or "retry" && now - items[i].CreatedAtUtc > TimeSpan.FromMinutes(30))
                { items[i] = items[i] with { Status = "expired", LastError = "Aviso con más de 30 minutos de antigüedad; consultar el incidente." }; changed = true; }
            var next = items.Where(i => i.Status is "awaiting-configuration" or "retry" &&
                (i.NextAttemptAtUtc is null || i.NextAttemptAtUtc <= now)).OrderBy(i => i.CreatedAtUtc).FirstOrDefault();
            if (next is not null)
            {
                var index = items.IndexOf(next);
                next = next with { Status = "sending", Attempts = next.Attempts + 1, NextAttemptAtUtc = null };
                items[index] = next; changed = true;
            }
            if (changed) Save();
            return next;
        }
    }

    public void Complete(string id, EmailDelivery delivery, DateTimeOffset now)
    {
        lock (gate)
        {
            var index = items.FindIndex(i => i.Id == id && i.Status == "sending");
            if (index < 0) return;
            var item = items[index];
            var status = delivery.State;
            if (status == "retry" && item.Attempts >= 5) status = "failed";
            items[index] = item with { Status = status, LastError = delivery.Error,
                ProviderRequestId = delivery.ProviderRequestId,
                AcceptedAtUtc = status == "accepted" ? now : null,
                NextAttemptAtUtc = status == "retry" ? now.AddSeconds(Math.Max(delivery.RetryAfterSeconds ?? 0,
                    Math.Min(900, 30 * Math.Pow(2, item.Attempts - 1)))) : null };
            Save();
        }
    }

    private void Save()
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items, json));
        File.Move(temp, path, true);
        if(events is not null)foreach(var item in items)events.Notification(item);
    }
}
