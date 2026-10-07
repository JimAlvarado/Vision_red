using System.Net;
using System.Text.Json;

public sealed record MobileInvitation(int Slot, string Email, DateTimeOffset SentAtUtc, string State = "accepted", string? RequestId = null);
public sealed record MobileInvitationRequest(string? Email, int? Slot, string? RequestId = null, bool ConfirmUncertain = false);
public sealed record MobileInvitationReceipt(string RequestId, int Slot, string Email, string State,
    DateTimeOffset RequestedAtUtc, DateTimeOffset UpdatedAtUtc, string? Error = null,
    string? ProviderRequestId = null, int? HttpStatus = null, string? ProviderErrorCode = null, string? ClientRequestId = null);

// Invitations to the mobile portal: the local editor sends one free code by email from the Vision mailbox.
public sealed class MobileInvitations(string dataDir, MobileAccess access, VpnMobileNetwork network, IManualEmailTransport email,
    NotificationOutbox outbox, EventRepository events, ILogger<MobileInvitations>? logger = null)
{
    private readonly object gate = new();
    private readonly string path = Path.Combine(dataDir, "mobile-invitations.json");
    private readonly string receiptsPath = Path.Combine(dataDir, "mobile-invitation-receipts.json");
    private readonly SemaphoreSlim operationGate = new(1);
    private List<MobileInvitationReceipt>? receipts;
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // Last invitation per code; kept only in datos and shown only in the local editor.
    public MobileInvitation[] Snapshot()
    {
        lock (gate) return Load().Concat(LoadReceipts().Where(i => i.State is "accepted" or "sending" or "unknown")
            .Select(i => new MobileInvitation(i.Slot, i.Email, i.UpdatedAtUtc, i.State == "sending" ? "unknown" : i.State, i.RequestId)))
            .GroupBy(i => i.Slot).Select(g => g.OrderByDescending(i => i.SentAtUtc).First()).OrderBy(i => i.Slot).ToArray();
    }

    // Local editor only. No codes, tokens or mail bodies are recorded.
    public MobileInvitationReceipt[] Receipts()
    {
        lock (gate) return LoadReceipts().Select(i => i.State == "sending" ? i with { State = "unknown" } : i).ToArray();
    }

    public async Task<IResult> SendAsync(MobileInvitationRequest input, CancellationToken token)
    {
        var address = input.Email?.Trim() ?? "";
        if (!EmailChannel.IsValidAddress(address))
            return Results.BadRequest(new { error = "Escribe un correo válido, con dominio completo (por ejemplo nombre@empresa.com)." });
        if (input.Slot is not { } slot || access.CodeForSlot(slot) is not { } code)
            return Results.BadRequest(new { error = "Elige un código de acceso." });
        if (input.RequestId is not null && !Guid.TryParse(input.RequestId, out _))
            return Results.BadRequest(new { error = "La referencia de la invitación no es válida. Actualiza la página." });
        var requestId = input.RequestId is null ? Guid.NewGuid().ToString() : Guid.Parse(input.RequestId).ToString();
        // Do not queue two invitations: the user must see and review the first result.
        if (!await operationGate.WaitAsync(0, token))
            return Results.Conflict(new { error = "Hay una invitación en curso. Espera su resultado antes de enviar otra." });
        try
        {
            lock (gate)
            {
                var previous = LoadReceipts().FirstOrDefault(i => i.RequestId == requestId);
                if (previous is not null)
                    return previous.Slot == slot && string.Equals(previous.Email, address, StringComparison.OrdinalIgnoreCase)
                        ? Reply(previous) : Results.Conflict(new { error = "Esta referencia corresponde a otra invitación." });
                if (!input.ConfirmUncertain && LoadReceipts().LastOrDefault(i => i.Slot == slot && i.State is "accepted" or "sending" or "unknown") is { State: "sending" or "unknown" })
                    return Results.Conflict(new { error = "Este código tiene un envío sin confirmar. Revisa Elementos enviados y la traza de correo antes de preparar otra invitación." });
            }
            if (Validate() is { } invalid) return ValidationReply(invalid);
            var now = DateTimeOffset.UtcNow;
            var receipt = new MobileInvitationReceipt(requestId, slot, address, "sending", now, now);
            var submitted = false;
            string? warning = null;
            EmailDelivery result;
            try
            {
                result = await email.SendDirectAsync(address, "Invitación al portal móvil de Vision", Compose(code, network.Url), token,
                    beforeSend: () =>
                    {
                        if (Validate() is { } blocked) return blocked;
                        // If this write fails, the transport must not submit the message.
                        lock (gate) SaveReceipt(receipt);
                        submitted = true;
                        return null;
                    },
                    afterSend: delivery =>
                    {
                        // Persist the mailbox pause while still holding the shared sending gate.
                        if (!delivery.Throttled && delivery.State != "accepted") return;
                        try { outbox.CompleteBatch([], delivery, DateTimeOffset.UtcNow); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        { warning = "No se pudo guardar el estado local del buzón. Revisa el registro de Vision."; Log(ex, requestId); }
                    }, requestId: requestId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or HttpRequestException)
            {
                Log(ex, requestId);
                if (!submitted)
                    return Results.Json(new { error = "No se inició el envío. No se pudo preparar el comprobante o se canceló la solicitud.", state = "failed", requestId }, statusCode: 503);
                result = new("unknown", "No se recibió confirmación. Revisa Elementos enviados antes de repetir.", ClientRequestId: requestId);
            }
            if (result.State == "blocked") return ValidationReply(result);
            receipt = receipt with { State = result.State == "retry" ? "failed" : result.State,
                UpdatedAtUtc = DateTimeOffset.UtcNow, Error = result.Error, ProviderRequestId = result.ProviderRequestId,
                HttpStatus = result.HttpStatus, ProviderErrorCode = result.ProviderErrorCode, ClientRequestId = result.ClientRequestId };
            try { lock (gate) SaveReceipt(receipt); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { warning = "No se pudo guardar el resultado final. Conserva esta referencia y revisa el registro antes de repetir."; Log(ex, requestId); }
            // Secondary records must never turn a confirmed external acceptance into a send failure.
            try
            {
                if (receipt.State == "accepted")
                    lock (gate) Save(Load().Where(i => i.Slot != slot).Append(new MobileInvitation(slot, address, receipt.UpdatedAtUtc, RequestId: requestId)).OrderBy(i => i.Slot).ToList());
                events.Add("mobile-invite:" + requestId, receipt.UpdatedAtUtc, "access", receipt.State == "accepted" ? "info" : "warning",
                    "mobile_invitation_" + (receipt.State == "accepted" ? "sent" : receipt.State), name: "Consulta móvil", reference: requestId,
                    description: $"Invitación del código {slot}: {receipt.State}. Aceptación de Microsoft no confirma entrega.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { warning ??= "El envío tiene comprobante, pero no se pudo actualizar el registro auxiliar. Revisa el registro de Vision."; Log(ex, requestId); }
            return Reply(receipt, warning);
        }
        finally { operationGate.Release(); }

        EmailDelivery? Validate()
        {
            if (access.SlotInUse(slot)) return new("blocked", $"El código {slot} está en uso. Elige un código libre o libera ese acceso.", HttpStatus: 409);
            if (!network.ListeningOnVpn) return new("blocked", "Vision no está escuchando en la dirección de acceso móvil. Revisa la red y reinicia Vision.", HttpStatus: 409);
            if (outbox.Limit is { PausedUntilUtc: { } until } && until > DateTimeOffset.UtcNow)
                return new("blocked", $"Microsoft limitó el envío del buzón. Intenta después de las {AlertMessage.FormatTime(until)}.", HttpStatus: 503);
            return null;
        }
    }

    private static IResult ValidationReply(EmailDelivery result) => Results.Json(new { error = result.Error }, statusCode: result.HttpStatus ?? 409);

    private static IResult Reply(MobileInvitationReceipt receipt, string? warning = null)
    {
        var state = receipt.State == "sending" ? "unknown" : receipt.State;
        return Results.Json(new { sent = state == "accepted", state, receipt.RequestId, receipt.Slot, receipt.Email,
            sentAtUtc = state == "accepted" ? receipt.UpdatedAtUtc : (DateTimeOffset?)null,
            error = state == "unknown" ? "El envío quedó sin confirmar. Revisa Elementos enviados o la traza de correo; no lo repitas automáticamente." : receipt.Error,
            warning }, statusCode: state == "accepted" ? 200 : state == "unknown" ? 502 : 503);
    }

    private void Log(Exception ex, string requestId) => logger?.LogWarning("Comprobante de invitación {RequestId}: {Type}. Revisar permisos y almacenamiento local.", requestId, ex.GetType().Name);

    private List<MobileInvitationReceipt> LoadReceipts() => receipts ??= File.Exists(receiptsPath)
        ? JsonSerializer.Deserialize<List<MobileInvitationReceipt>>(File.ReadAllText(receiptsPath), json)
            ?? throw new InvalidDataException("El archivo de comprobantes está vacío. Conserva el archivo y revisa su respaldo.") : [];

    private void SaveReceipt(MobileInvitationReceipt receipt)
    {
        var next = LoadReceipts().Where(i => i.RequestId != receipt.RequestId).Append(receipt).ToList();
        SaveFile(receiptsPath, JsonSerializer.Serialize(next, json));
        receipts = next;
    }

    private List<MobileInvitation> Load()
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<List<MobileInvitation>>(File.ReadAllText(path), json) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    private void Save(List<MobileInvitation> items)
    {
        SaveFile(path, JsonSerializer.Serialize(items, json));
    }

    private static void SaveFile(string target, string content)
    {
        var temporary = target + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(content);
            stream.Write(bytes);
            stream.Flush(true);
        }
        File.Move(temporary, target, true);
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
            "<h2 style=\"font-size:18px\">Cómo entrar</h2><ol style=\"line-height:1.7\"><li>Conecta tu celular o tablet a la red de Arzyz o a la VPN corporativa.</li><li>Abre la dirección en el navegador.</li><li>Escribe el código de acceso.</li></ol>" +
            "<h2 style=\"font-size:18px\">Importante</h2><ul style=\"line-height:1.7\"><li>El código funciona en un solo dispositivo o navegador a la vez y no caduca.</li>" +
            "<li>Para usarlo en otro dispositivo, cierra la sesión en el anterior o pide al administrador que libere el acceso.</li>" +
            "<li>Si borras las cookies del navegador o usas modo privado, tendrás que pedir que se libere.</li><li>No compartas este código.</li></ul>" +
            "<p style=\"font-size:12px;color:#64748b\">Invitación enviada por el administrador de Vision. Si no esperabas este correo, ignóralo.</p></div></body></html>";
    }
}
