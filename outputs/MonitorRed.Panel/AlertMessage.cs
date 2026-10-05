using System.Net;

public static class AlertMessage
{
    public const string AccessIp = "monitor-access";
    public const string SubjectPrefix = "[VISION-APODACA]";
    public static string Subject(string text) => text.StartsWith(SubjectPrefix, StringComparison.OrdinalIgnoreCase)
        ? text : $"{SubjectPrefix} {text}";
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
    public static (string Subject, string Html) Compose(Incident incident, string kind)
    {
        var recovery = kind == "recovery";
        var network = incident.Ip == AccessIp;
        var title = network ? recovery ? "Acceso a la red restablecido" : "Pérdida de acceso a la red monitoreada" :
            recovery ? "Conectividad restablecida" : "Caída confirmada";
        var subject = Subject(title + (network ? "" : $" | {incident.Name} ({incident.Ip})"));
        string Encode(string value) => WebUtility.HtmlEncode(value);
        string Time(DateTimeOffset time) => FormatTime(time);
        var explanation = network ? recovery ? "Vision volvió a obtener respuesta de la red monitoreada." :
            "Vision perdió acceso a los equipos de referencia. Puede deberse a la VPN, una ruta de red o infraestructura compartida. No se ha confirmado una falla individual de cada switch." :
            recovery ? "Vision confirmó que el equipo volvió a responder a las comprobaciones de conectividad." :
            "Vision confirmó que el equipo dejó de responder a las comprobaciones de conectividad desde el monitor. Esto no determina por sí solo que esté apagado.";
        var action = recovery ? "Verificar la operación de los servicios asociados y revisar el historial si la interrupción se repite." :
            network ? "Revisar la conexión VPN y la ruta desde la PC que ejecuta Vision antes de intervenir los switches." :
            "Revisar alimentación, enlaces y acceso de red del equipo. Confirmar el diagnóstico antes de intervenirlo.";
        var end = incident.ClosedAtUtc ?? incident.OpenedAtUtc;
        var duration = incident.ClosedAtUtc is null ? "En curso" : FormatDuration(end - incident.OpenedAtUtc);
        var rows = $"<tr><td>Ubicación</td><td>Apodaca</td></tr><tr><td>Equipo / alcance</td><td>{Encode(incident.Name)}</td></tr>" +
            (network ? "" : $"<tr><td>Dirección IP</td><td>{Encode(incident.Ip)}</td></tr>") +
            $"<tr><td>Inicio confirmado</td><td>{Time(incident.OpenedAtUtc)}</td></tr>" +
            (recovery ? $"<tr><td>Recuperación confirmada</td><td>{Time(end)}</td></tr>" : "") +
            $"<tr><td>Duración registrada</td><td>{duration}</td></tr><tr><td>Comprobación</td><td>ICMP (ping); 3 fallos para pérdida y 2 respuestas para recuperación</td></tr>" +
            $"<tr><td>Referencia</td><td>{Encode(incident.Id)}</td></tr>";
        return (subject, "<!doctype html><html lang=\"es\"><body style=\"margin:0;background:#f1f5f9;font:16px Arial,sans-serif;color:#1e293b\">" +
            "<div style=\"max-width:640px;margin:24px auto;padding:28px;background:#fff;border-radius:12px\">" +
            "<p style=\"color:#475569;font-size:13px\">VISION · MONITOREO DE RED · APODACA</p>" +
            $"<h1 style=\"font-size:24px;color:{(recovery ? "#087f5b" : "#b42318")}\">{title}</h1>" +
            $"<p>Estimado equipo de Sistemas:</p><p style=\"line-height:1.6\">{explanation}</p>" +
            $"<table style=\"width:100%;border-collapse:collapse;font-size:14px\" cellpadding=\"10\" border=\"1\">{rows}</table>" +
            $"<h2 style=\"font-size:18px\">Acción recomendada</h2><p style=\"line-height:1.6\">{action}</p>" +
            "<p style=\"font-size:12px;color:#64748b\">Notificación automática de Vision. Las horas corresponden a la confirmación del monitor, no necesariamente al instante exacto de la interrupción. Si el mensaje llega con demora, consultar el estado actual del panel.</p></div></body></html>");
    }
    // One message for several changes: simultaneous losses/recoveries and intermittent devices.
    public static PendingNotification ComposeDigest(IReadOnlyList<PendingNotification> batch, NotificationOutbox outbox, DateTimeOffset now)
    {
        string Encode(string value) => WebUtility.HtmlEncode(value);
        string Count(int value, string one, string many) => $"{value} {(value == 1 ? one : many)}";
        string Label(PendingNotification item) => item.Ip == AccessIp
            ? item.Kind == "recovery" ? "Acceso a la red restablecido" : "Pérdida de acceso a la red"
            : item.Kind == "recovery" ? "Conectividad restablecida" : "Caída confirmada";
        var groups = batch.GroupBy(i => i.Ip).ToList();
        var rows = new List<string>();
        var intermittent = 0;
        var stillDown = new List<string>();
        foreach (var group in groups)
        {
            var ordered = group.OrderBy(i => i.CreatedAtUtc).ToList();
            var first = ordered[0];
            var last = ordered[^1];
            var ip = first.Ip == AccessIp ? "—" : first.Ip;
            if (last.Kind == "down") stillDown.Add(first.Name);
            if (ordered.Count >= 2 && outbox.IsIntermittent(first.Ip, now))
            {
                intermittent++;
                var losses = ordered.Count(i => i.Kind == "down");
                var recoveries = ordered.Count(i => i.Kind == "recovery");
                rows.Add($"<tr><td><b>Intermitente</b></td><td>{Encode(first.Name)}</td><td>{Encode(ip)}</td><td>{FormatTime(first.CreatedAtUtc)}<br>a {FormatTime(last.CreatedAtUtc)}</td>" +
                    $"<td>{Count(losses, "pérdida", "pérdidas")} y {Count(recoveries, "recuperación", "recuperaciones")}. Último estado: {Encode(Label(last))}.</td></tr>");
                continue;
            }
            foreach (var item in ordered)
            {
                var detail = "";
                if (item.Kind == "recovery" && outbox.OpenedAt(item.IncidentId) is { } opened)
                    detail = "Duración: " + FormatDuration(item.CreatedAtUtc - opened);
                rows.Add($"<tr><td><b style=\"color:{(item.Kind == "recovery" ? "#087f5b" : "#b42318")}\">{Encode(Label(item))}</b></td><td>{Encode(item.Name)}</td><td>{Encode(ip)}</td><td>{FormatTime(item.CreatedAtUtc)}</td><td>{detail}</td></tr>");
            }
        }
        var downs = batch.Count(i => i.Kind == "down");
        var recoveriesTotal = batch.Count(i => i.Kind == "recovery");
        var parts = new List<string>();
        if (downs > 0) parts.Add(Count(downs, "caída", "caídas"));
        if (recoveriesTotal > 0) parts.Add(Count(recoveriesTotal, "recuperación", "recuperaciones"));
        if (intermittent > 0) parts.Add(Count(intermittent, "equipo intermitente", "equipos intermitentes"));
        var network = batch.Any(i => i.Ip == AccessIp && i.Kind == "down") ? "Acceso a la red | " : "";
        var subject = Subject($"Resumen de red | {network}{string.Join(", ", parts)}");
        var from = batch.Min(i => i.CreatedAtUtc);
        var to = batch.Max(i => i.CreatedAtUtc);
        var state = stillDown.Count == 0
            ? "<p style=\"line-height:1.6;color:#087f5b\"><b>Al cierre del resumen, todos los equipos incluidos respondían.</b></p>"
            : $"<p style=\"line-height:1.6;color:#b42318\"><b>Al cierre del resumen seguían sin respuesta:</b> {Encode(string.Join(", ", stillDown))}.</p>";
        var delayed = batch.Any(i => i.DelayedByLimit) || now - from > TimeSpan.FromMinutes(10)
            ? "<p style=\"line-height:1.6\">Algunos cambios se envían con demora porque Microsoft limitó el envío o porque el equipo estuvo intermitente. Consultar el panel para el estado actual.</p>"
            : "";
        var html = "<!doctype html><html lang=\"es\"><body style=\"margin:0;background:#f1f5f9;font:16px Arial,sans-serif;color:#1e293b\">" +
            "<div style=\"max-width:720px;margin:24px auto;padding:28px;background:#fff;border-radius:12px\">" +
            "<p style=\"color:#475569;font-size:13px\">VISION · MONITOREO DE RED · APODACA</p>" +
            $"<h1 style=\"font-size:24px;color:{(stillDown.Count > 0 ? "#b42318" : "#087f5b")}\">Resumen de cambios de red</h1>" +
            $"<p>Estimado equipo de Sistemas:</p><p style=\"line-height:1.6\">Vision agrupó {Count(batch.Count, "cambio", "cambios")} detectados entre {FormatTime(from)} y {FormatTime(to)} para no enviar un correo por equipo.</p>" +
            state + delayed +
            "<table style=\"width:100%;border-collapse:collapse;font-size:14px\" cellpadding=\"8\" border=\"1\">" +
            "<tr style=\"background:#f8fafc\"><th align=\"left\">Evento</th><th align=\"left\">Equipo / alcance</th><th align=\"left\">Dirección IP</th><th align=\"left\">Hora confirmada</th><th align=\"left\">Detalle</th></tr>" +
            string.Concat(rows) + "</table>" +
            "<h2 style=\"font-size:18px\">Acción recomendada</h2><p style=\"line-height:1.6\">Revisar primero los equipos que siguen sin respuesta. Una pérdida de acceso a la red puede deberse a la VPN o a una ruta compartida; los equipos intermitentes conviene revisarlos por enlace o alimentación.</p>" +
            "<p style=\"font-size:12px;color:#64748b\">Notificación automática de Vision. Comprobación ICMP (ping): 3 fallos para pérdida y 2 respuestas para recuperación. Las horas corresponden a la confirmación del monitor.</p></div></body></html>";
        return new("digest-" + Guid.NewGuid().ToString("N"), "", "", "Resumen de red", "digest", "email", now, "sending", subject, html);
    }
    public static string FormatTime(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, Zone).ToString("dd/MM/yyyy hh:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture) + " (Ciudad de México)";
    private static string FormatDuration(TimeSpan span) =>
        $"{(int)Math.Max(0, span.TotalHours)} h {Math.Max(0, span.Minutes)} min {Math.Max(0, span.Seconds)} s";
}
