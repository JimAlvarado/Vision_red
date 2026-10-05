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
    public static string FormatTime(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, Zone).ToString("dd/MM/yyyy hh:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture) + " (Ciudad de México)";
    private static string FormatDuration(TimeSpan span) =>
        $"{(int)Math.Max(0, span.TotalHours)} h {Math.Max(0, span.Minutes)} min {Math.Max(0, span.Seconds)} s";
}
