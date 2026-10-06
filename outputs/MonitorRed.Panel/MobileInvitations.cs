using System.Net;
using System.Text.Json;

public sealed record MobileInvitation(int Slot, string Email, DateTimeOffset SentAtUtc);
public sealed record MobileInvitationRequest(string? Email, int? Slot);

// Invitations to the mobile portal: the local editor sends one free code by email from the Vision mailbox.
public sealed class MobileInvitations(string dataDir, MobileAccess access, VpnMobileNetwork network, EmailChannel email,
    NotificationOutbox outbox, EventRepository events)
{
    private readonly object gate = new();
    private readonly string path = Path.Combine(dataDir, "mobile-invitations.json");
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // Last invitation per code; kept only in datos and shown only in the local editor.
    public MobileInvitation[] Snapshot()
    {
        lock (gate) return Load().ToArray();
    }

    public async Task<IResult> SendAsync(MobileInvitationRequest input, CancellationToken token)
    {
        var address = input.Email?.Trim() ?? "";
        if (!EmailChannel.IsValidAddress(address))
            return Results.BadRequest(new { error = "Escribe un correo válido, con dominio completo (por ejemplo nombre@empresa.com)." });
        if (input.Slot is not { } slot || access.CodeForSlot(slot) is not { } code)
            return Results.BadRequest(new { error = "Elige un código de acceso." });
        if (access.SlotInUse(slot))
            return Results.Conflict(new { error = $"El código {slot} está en uso. Elige un código libre o libera ese acceso." });
        if (!network.ListeningOnVpn)
            return Results.Conflict(new { error = "Vision no está escuchando en la VPN; la persona invitada no podría entrar. Revisa la VPN y reinicia Vision." });
        var now = DateTimeOffset.UtcNow;
        if (outbox.Limit is { PausedUntilUtc: { } until } && until > now)
            return Results.Json(new { error = $"Microsoft limitó el envío del buzón. Intenta después de las {AlertMessage.FormatTime(until)}." }, statusCode: 503);

        var result = await email.SendDirectAsync(address, "Invitación al portal móvil de Vision", Compose(code, network.Url), token);
        // A limit or an acceptance also applies to automatic alerts: they share the mailbox.
        if (result.Throttled || result.State == "accepted") outbox.CompleteBatch([], result, DateTimeOffset.UtcNow);
        if (result.State != "accepted")
        {
            events.Add(Guid.NewGuid().ToString(), now, "access", "warning", "mobile_invitation_failed", name: "Consulta móvil",
                description: $"Invitación al portal móvil no enviada (código {slot}). {result.Error}");
            var message = result.Throttled ? "Microsoft limitó el envío del buzón; intenta más tarde." : result.Error ?? "No se pudo enviar la invitación.";
            return Results.Json(new { error = message }, statusCode: result.State == "unknown" ? 502 : 503);
        }
        lock (gate)
            Save(Load().Where(i => i.Slot != slot).Append(new MobileInvitation(slot, address, now)).OrderBy(i => i.Slot).ToList());
        events.Add(Guid.NewGuid().ToString(), now, "access", "info", "mobile_invitation_sent", name: "Consulta móvil",
            description: $"Invitación al portal móvil enviada con el código {slot}. Microsoft aceptó el envío.");
        return Results.Ok(new { sent = true, slot, email = address, sentAtUtc = now });
    }

    private List<MobileInvitation> Load()
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<MobileInvitation>>(File.ReadAllText(path), json) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    private void Save(List<MobileInvitation> items)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(items, json));
        File.Move(temporary, path, true);
    }

    public static string Compose(string code, string url)
    {
        string Encode(string value) => WebUtility.HtmlEncode(value);
        return "<!doctype html><html lang=\"es\"><body style=\"margin:0;background:#f1f5f9;font:16px Arial,sans-serif;color:#1e293b\">" +
            "<div style=\"max-width:640px;margin:24px auto;padding:28px;background:#fff;border-radius:12px\">" +
            "<p style=\"color:#475569;font-size:13px\">VISION · MONITOREO DE RED · APODACA</p>" +
            "<h1 style=\"font-size:24px;color:#0f766e\">Invitación al portal móvil</h1>" +
            "<p style=\"line-height:1.6\">Hola:</p><p style=\"line-height:1.6\">Se te dio acceso a la consulta móvil de Vision. Desde tu celular o tablet podrás ver el estado de los equipos, el mapa de red y los incidentes.</p>" +
            "<table style=\"width:100%;border-collapse:collapse;font-size:15px\" cellpadding=\"10\" border=\"1\">" +
            $"<tr><td>Código de acceso</td><td><b style=\"font-size:22px;letter-spacing:3px;font-family:Consolas,monospace\">{Encode(code)}</b></td></tr>" +
            $"<tr><td>Dirección</td><td><a href=\"{Encode(url)}\">{Encode(url)}</a></td></tr></table>" +
            "<h2 style=\"font-size:18px\">Cómo entrar</h2><ol style=\"line-height:1.7\"><li>Conecta tu celular o tablet a la VPN corporativa.</li><li>Abre la dirección en el navegador.</li><li>Escribe el código de acceso.</li></ol>" +
            "<h2 style=\"font-size:18px\">Importante</h2><ul style=\"line-height:1.7\"><li>El código funciona en un solo dispositivo o navegador a la vez y no caduca.</li>" +
            "<li>Para usarlo en otro dispositivo, cierra la sesión en el anterior o pide al administrador que libere el acceso.</li>" +
            "<li>Si borras las cookies del navegador o usas modo privado, tendrás que pedir que se libere.</li><li>No compartas este código.</li></ul>" +
            "<p style=\"font-size:12px;color:#64748b\">Invitación enviada por el administrador de Vision. Si no esperabas este correo, ignóralo.</p></div></body></html>";
    }
}
