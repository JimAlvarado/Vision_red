using System.Net;
using System.Text.Json;

public sealed record OwnerNotificationSettings(string RecipientAddress, bool Enabled);
public sealed class OwnerNotifications
{
    private readonly NotificationOutbox outbox;
    public string? Address { get; }
    public bool Enabled { get; }
    public OwnerNotifications(string dataDir, NotificationOutbox outbox)
    {
        this.outbox = outbox;
        var path = Path.Combine(dataDir, "owner-notifications.json");
        if (!File.Exists(path)) return;
        var settings = JsonSerializer.Deserialize<OwnerNotificationSettings>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Falta la configuración de avisos al autor.");
        if (!System.Net.Mail.MailAddress.TryCreate(settings.RecipientAddress, out var parsed) || parsed.Address != settings.RecipientAddress)
            throw new InvalidDataException("El correo del autor no es válido.");
        Address = settings.RecipientAddress;
        Enabled = settings.Enabled;
    }
    public void RecipientsAdded(string[] addresses, DateTimeOffset now, string? eventId = null)
    {
        if (!Enabled || addresses.Length == 0) return;
        var rows = string.Join("", addresses.Select(address => "<li>" + WebUtility.HtmlEncode(address) + "</li>"));
        Queue("recipient_added", "Nuevo destinatario", AlertMessage.Subject("Nuevo destinatario"),
            $"<p>Se agregaron estos correos a los destinatarios de Vision:</p><ul>{rows}</ul><p>Operación realizada desde el editor local. El editor no identifica a la persona que realizó el cambio.</p>", "", now, eventId);
    }
    public void MobileLogin(string ip, string userAgent, DateTimeOffset now)
    {
        if (!Enabled) return;
        var agent = new string(userAgent.Where(c => !char.IsControl(c)).Take(240).ToArray());
        var device = agent.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "Tablet iPad" :
            agent.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ? "Celular iPhone" :
            agent.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Dispositivo Android" :
            agent.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Equipo Windows" : "Dispositivo de consulta";
        Queue("mobile_login", device, AlertMessage.Subject("Acceso móvil"),
            $"<p>Se inició sesión correctamente en la consulta de Vision.</p><p><b>Dispositivo declarado:</b> {WebUtility.HtmlEncode(device)}<br><b>IP de conexión:</b> {WebUtility.HtmlEncode(ip)}<br><b>Navegador declarado:</b> {WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(agent) ? "No disponible" : agent)}</p><p>El código es compartido. Estos datos no identifican con certeza a una persona ni el número o modelo del teléfono.</p>", ip, now);
    }
    private void Queue(string kind, string name, string subject, string content, string ip, DateTimeOffset now, string? eventId = null)
    {
        var message = "<!doctype html><html lang=\"es\"><body style=\"font-family:Segoe UI,Arial;color:#182333\"><h2>Vision · Aviso al autor</h2>" +
            content + $"<p><b>Fecha:</b> {WebUtility.HtmlEncode(AlertMessage.FormatTime(now))} · Ciudad de México</p></body></html>";
        outbox.EnqueueOwner(eventId ?? Guid.NewGuid().ToString("N"), kind, ip, name, now, subject, message, Address!);
    }
}
