using System.Text.Json;

public sealed record PendingNotification(string Id, string IncidentId, string Ip, string Name,
    string Kind, string Channel, DateTimeOffset CreatedAtUtc, string Status, string Subject, string Message,
    int Attempts = 0, DateTimeOffset? NextAttemptAtUtc = null, DateTimeOffset? AcceptedAtUtc = null,
    string? LastError = null, string? ProviderRequestId = null, string[]? TargetRecipients = null,
    bool DelayedByLimit = false)
{
    public bool OwnerOnly => Kind is "recipient_added" or "mobile_login";
}

// Global pause after Microsoft limits the mailbox (HTTP 429); persisted so a restart does not resume early.
public sealed record SendingLimit(DateTimeOffset? PausedUntilUtc, int ConsecutiveLimits, string? Reason);

public sealed class NotificationOutbox
{
    // Simultaneous changes are collected for a short window and sent as one message.
    public static readonly TimeSpan GroupWindow = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan GroupQuiet = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan GroupMaxWait = TimeSpan.FromSeconds(60);
    // A device with two or more losses in 30 minutes is intermittent: its changes wait for a summary.
    public static readonly TimeSpan IntermittentWindow = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan IntermittentStable = TimeSpan.FromMinutes(10);
    public const int MaxBatch = 40;

    private readonly object gate = new();
    private readonly string path;
    private readonly string limitPath;
    private readonly List<PendingNotification> items;
    private readonly EventRepository? events;
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private SendingLimit limit = new(null, 0, null);

    public NotificationOutbox(string path, EventRepository? events = null)
    {
        this.path = path;
        this.events = events;
        limitPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "correo-limite.json");
        items = File.Exists(path)
            ? JsonSerializer.Deserialize<List<PendingNotification>>(File.ReadAllText(path), json) ?? []
            : [];
        try { if (File.Exists(limitPath)) limit = JsonSerializer.Deserialize<SendingLimit>(File.ReadAllText(limitPath), json) ?? limit; }
        catch (JsonException) { }
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

    public SendingLimit Limit
    {
        get { lock (gate) return limit; }
    }

    public bool IsLimited(DateTimeOffset now)
    {
        lock (gate) return limit.PausedUntilUtc > now;
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

    public void EnqueueOwner(string eventId, string kind, string ip, string name, DateTimeOffset now,
        string subject, string message, string ownerAddress)
    {
        if (kind is not ("recipient_added" or "mobile_login")) throw new ArgumentException("Tipo de aviso inválido.");
        lock (gate)
        {
            if (items.Any(i => i.Id == eventId)) return;
            items.Add(new(eventId, eventId, ip, name, kind, "email", now, "awaiting-configuration", subject, message,
                TargetRecipients: [ownerAddress]));
            Save();
        }
    }

    public int CountOwner(string kind, DateTimeOffset since, Func<PendingNotification, bool>? match = null)
    {
        lock (gate) return items.Count(i => i.Kind == kind && i.CreatedAtUtc >= since && (match is null || match(i)));
    }

    // Opening time of the incident behind a recovery, when its loss notice is still in the queue.
    public DateTimeOffset? OpenedAt(string incidentId)
    {
        lock (gate) return items.FirstOrDefault(i => i.IncidentId == incidentId && i.Kind == "down")?.CreatedAtUtc;
    }

    public bool IsIntermittent(string ip, DateTimeOffset now)
    {
        lock (gate) return Intermittent(ip, now);
    }

    public PendingNotification? Claim(DateTimeOffset now, bool automaticEnabled = true, bool ownerEnabled = true) =>
        ClaimBatch(now, automaticEnabled, ownerEnabled, 1).FirstOrDefault();

    // Network alerts go first and together; owner notices go one at a time when no network batch is ready.
    public PendingNotification[] ClaimBatch(DateTimeOffset now, bool automaticEnabled = true, bool ownerEnabled = true,
        int maxItems = MaxBatch)
    {
        lock (gate)
        {
            var changed = false;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Status is not ("awaiting-configuration" or "retry")) continue;
                var maxAge = item.OwnerOnly ? TimeSpan.FromHours(24) :
                    item.DelayedByLimit ? TimeSpan.FromHours(3) :
                    automaticEnabled && Intermittent(item.Ip, now) ? TimeSpan.FromMinutes(60) : TimeSpan.FromMinutes(30);
                if (now - item.CreatedAtUtc > maxAge)
                { items[i] = item with { Status = "expired", LastError = "Aviso antiguo sin enviar; consultar el historial." }; changed = true; }
            }
            PendingNotification[] claimed = [];
            if (!(limit.PausedUntilUtc > now))
            {
                var due = items.Where(i => i.Status is "awaiting-configuration" or "retry" &&
                    (i.OwnerOnly ? ownerEnabled : automaticEnabled) &&
                    (i.NextAttemptAtUtc is null || i.NextAttemptAtUtc <= now)).ToList();
                var network = due.Where(i => !i.OwnerOnly && !Held(i, now)).OrderBy(i => i.CreatedAtUtc).ToList();
                if (network.Count > 0 && (maxItems == 1 || GroupReady(network, now)))
                    claimed = network.Take(Math.Max(1, maxItems)).ToArray();
                else
                {
                    var owner = due.Where(i => i.OwnerOnly).OrderBy(i => i.CreatedAtUtc).FirstOrDefault();
                    if (owner is not null) claimed = [owner];
                }
            }
            for (var c = 0; c < claimed.Length; c++)
            {
                var index = items.IndexOf(claimed[c]);
                claimed[c] = claimed[c] with { Status = "sending", Attempts = claimed[c].Attempts + 1, NextAttemptAtUtc = null };
                items[index] = claimed[c]; changed = true;
            }
            if (changed) Save();
            return claimed;
        }
    }

    public void Complete(string id, EmailDelivery delivery, DateTimeOffset now) => CompleteBatch([id], delivery, now);

    public void CompleteBatch(IReadOnlyCollection<string> ids, EmailDelivery delivery, DateTimeOffset now)
    {
        lock (gate)
        {
            if (delivery.Throttled)
            {
                // Microsoft limited the mailbox: pause every send and do not count this attempt.
                var consecutive = limit.ConsecutiveLimits + 1;
                var seconds = Math.Max(delivery.RetryAfterSeconds ?? 0, Math.Min(3600, 60 * Math.Pow(2, consecutive - 1)));
                limit = new(now.AddSeconds(seconds), consecutive, delivery.Error);
                SaveLimit();
            }
            else if (delivery.State == "accepted" && limit.ConsecutiveLimits > 0)
            {
                limit = new(null, 0, null);
                SaveLimit();
            }
            foreach (var id in ids)
            {
                var index = items.FindIndex(i => i.Id == id && i.Status == "sending");
                if (index < 0) continue;
                var item = items[index];
                if (delivery.Throttled)
                {
                    items[index] = item with { Status = "retry", Attempts = Math.Max(0, item.Attempts - 1), DelayedByLimit = true,
                        LastError = delivery.Error, ProviderRequestId = delivery.ProviderRequestId, NextAttemptAtUtc = limit.PausedUntilUtc };
                    continue;
                }
                var status = delivery.State;
                if (status == "retry" && item.Attempts >= 5) status = "failed";
                items[index] = item with { Status = status, LastError = delivery.Error,
                    ProviderRequestId = delivery.ProviderRequestId,
                    AcceptedAtUtc = status == "accepted" ? now : null,
                    NextAttemptAtUtc = status == "retry" ? now.AddSeconds(Math.Max(delivery.RetryAfterSeconds ?? 0,
                        Math.Min(900, 30 * Math.Pow(2, item.Attempts - 1)))) : null };
            }
            Save();
        }
    }

    private bool Intermittent(string ip, DateTimeOffset now) =>
        items.Count(i => !i.OwnerOnly && i.Ip == ip && i.Kind == "down" && now - i.CreatedAtUtc <= IntermittentWindow) >= 2;

    // Changes of an intermittent device wait until it is stable or the oldest one reaches the window.
    private bool Held(PendingNotification item, DateTimeOffset now)
    {
        if (!Intermittent(item.Ip, now)) return false;
        var related = items.Where(i => !i.OwnerOnly && i.Ip == item.Ip).ToList();
        var lastChange = related.Max(i => i.CreatedAtUtc);
        var oldestPending = related.Where(i => i.Status is "awaiting-configuration" or "retry").Min(i => i.CreatedAtUtc);
        return now - lastChange < IntermittentStable && now - oldestPending < IntermittentWindow;
    }

    private static bool GroupReady(List<PendingNotification> network, DateTimeOffset now)
    {
        var oldest = network.Min(i => i.CreatedAtUtc);
        var newest = network.Max(i => i.CreatedAtUtc);
        return now - oldest >= GroupMaxWait || (now - oldest >= GroupWindow && now - newest >= GroupQuiet);
    }

    private void SaveLimit()
    {
        var temp = limitPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(limit, json));
        File.Move(temp, limitPath, true);
        events?.Add($"email-limit:{limit.PausedUntilUtc:O}:{limit.ConsecutiveLimits}", DateTimeOffset.UtcNow, "email",
            limit.PausedUntilUtc is null ? "success" : "warning", limit.PausedUntilUtc is null ? "email_limit_cleared" : "email_limited",
            description: limit.PausedUntilUtc is null ? "Microsoft volvió a aceptar envíos; la pausa general terminó."
                : $"Microsoft limitó el envío. Pausa general hasta {AlertMessage.FormatTime(limit.PausedUntilUtc.Value)} (límite consecutivo {limit.ConsecutiveLimits}).");
    }

    private void Save()
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(items, json));
        File.Move(temp, path, true);
        if(events is not null)foreach(var item in items)events.Notification(item);
    }
}
