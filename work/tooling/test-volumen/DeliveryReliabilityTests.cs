using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

static class DeliveryReliabilityTests
{
    public static async Task Run(string root, Action<bool, string> check)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Las pruebas de accesos requieren Windows y DPAPI.");
        var ip = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Select(a => a.Address)
            .First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                (a.GetAddressBytes()[0] == 10 || a.GetAddressBytes()[0] == 192 && a.GetAddressBytes()[1] == 168 ||
                 a.GetAddressBytes()[0] == 172 && a.GetAddressBytes()[1] is >= 16 and <= 31));
        var counter = 0;
        (string Dir, MobileAccess Access, NotificationOutbox Box, EventRepository Events, VpnMobileNetwork Network) Fixture()
        {
            var dir = Path.Combine(root, "reliable-" + ++counter);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "mobile-access.dpapi"), ProtectedData.Protect(
                Encoding.UTF8.GetBytes("ABC123DEF456"), null, DataProtectionScope.CurrentUser));
            File.WriteAllText(Path.Combine(dir, "mobile-vpn-settings.json"), JsonSerializer.Serialize(new VpnMobileSettings(ip.ToString(), "10.250.0.0/24"), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            var events = new EventRepository(dir);
            return (dir, new MobileAccess(dir, events), new NotificationOutbox(Path.Combine(dir, "queue.json")), events, new VpnMobileNetwork(dir));
        }
        MobileInvitations Invites((string Dir, MobileAccess Access, NotificationOutbox Box, EventRepository Events, VpnMobileNetwork Network) f, IManualEmailTransport transport) =>
            new(f.Dir, f.Access, f.Network, transport, f.Box, f.Events, NullLogger<MobileInvitations>.Instance);
        int Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode ?? 0;
        JsonElement Value(IResult result) => JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        void Login(MobileAccess access, int slot)
        {
            var context = new DefaultHttpContext(); context.Connection.RemoteIpAddress = IPAddress.Loopback;
            access.Login(context, new MobileLogin(access.CodeForSlot(slot)));
        }

        // Acceptance has a durable pre-send receipt and can be retrieved after restart without resending.
        {
            var f = Fixture(); var id = Guid.NewGuid().ToString();
            var transport = new ManualTransport(new("accepted", ProviderRequestId: "provider-test", HttpStatus: 202, ClientRequestId: id));
            var sawPending = false;
            transport.OnSubmit = () => sawPending = File.ReadAllText(Path.Combine(f.Dir, "mobile-invitation-receipts.json")).Contains("sending");
            var invites = Invites(f, transport); var codes = f.Access.LocalAccessCodes.ToArray();
            var result = await invites.SendAsync(new("person@example.com", 2, id), CancellationToken.None);
            check(Status(result) == 200 && sawPending, "Comprobante: durable antes de aceptar, HTTP 200 después de aceptación");
            var receipt = invites.Receipts().Single();
            check(receipt is { State: "accepted", ProviderRequestId: "provider-test", HttpStatus: 202 } && receipt.ClientRequestId == id,
                "Comprobante: conserva fecha, referencia y HTTP del proveedor");
            check(Status(await Invites(f, transport).SendAsync(new("person@example.com", 2, id), CancellationToken.None)) == 200 && transport.Submissions == 1,
                "Duplicados: repetir referencia después de reiniciar no vuelve a enviar");
            check(Status(await invites.SendAsync(new("other@example.com", 2, id), CancellationToken.None)) == 409 && transport.Submissions == 1,
                "Duplicados: no reutiliza una referencia para otra persona");
            var stored = File.ReadAllText(Path.Combine(f.Dir, "mobile-invitation-receipts.json"));
            check(codes.All(c => !stored.Contains(c)) && !stored.Contains("<html") && codes.SequenceEqual(f.Access.LocalAccessCodes),
                "Privacidad: comprobantes sin códigos/cuerpos y accesos sin cambios");
        }
        // Event append or legacy snapshot failure cannot disguise acceptance as a send failure.
        {
            var f = Fixture(); var transport = new ManualTransport(new("accepted"));
            using var held = new FileStream(Path.Combine(f.Events.DirectoryPath, "events.csv"), FileMode.Open, FileAccess.Read, FileShare.Read);
            var invites = Invites(f, transport);
            var result = await invites.SendAsync(new("person@example.com", 2), CancellationToken.None);
            check(Status(result) == 200 && Value(result).GetProperty("warning").ValueKind == JsonValueKind.String && invites.Receipts().Single().State == "accepted",
                "Aceptación: fallo de registro CSV devuelve éxito con advertencia y comprobante");
        }
        {
            var f = Fixture(); var transport = new ManualTransport(new("accepted"));
            File.WriteAllText(Path.Combine(f.Dir, "mobile-invitations.json"), "[]");
            using var held = new FileStream(Path.Combine(f.Dir, "mobile-invitations.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
            var invites = Invites(f, transport);
            var result = await invites.SendAsync(new("person@example.com", 2), CancellationToken.None);
            check(Status(result) == 200 && invites.Snapshot().Single().State == "accepted" && invites.Receipts().Single().State == "accepted",
                "Aceptación: fallo del registro anterior conserva el resultado en el nuevo comprobante");
        }
        // Cannot submit without the durable intent. Ambiguous final writes stay ambiguous after restart.
        {
            var f = Fixture(); Directory.CreateDirectory(Path.Combine(f.Dir, "mobile-invitation-receipts.json.tmp"));
            var transport = new ManualTransport(new("accepted"));
            check(Status(await Invites(f, transport).SendAsync(new("person@example.com", 2), CancellationToken.None)) == 503 && transport.Submissions == 0,
                "Persistencia: si no puede guardar antes de enviar, no envía");
        }
        {
            var f = Fixture(); var transport = new ManualTransport(new("accepted")); FileStream? held = null;
            transport.OnSubmit = () => held = new FileStream(Path.Combine(f.Dir, "mobile-invitation-receipts.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
            var id = Guid.NewGuid().ToString(); var invites = Invites(f, transport);
            try
            {
                var result = await invites.SendAsync(new("person@example.com", 2, id), CancellationToken.None);
                check(Status(result) == 200 && Value(result).GetProperty("warning").ValueKind == JsonValueKind.String,
                    "Persistencia: aceptación con fallo del comprobante final se informa como aceptada con advertencia");
            }
            finally { held?.Dispose(); }
            var restarted = Invites(f, transport);
            check(restarted.Receipts().Single().State == "unknown" && Status(await restarted.SendAsync(new("person@example.com", 2, id), CancellationToken.None)) == 502 && transport.Submissions == 1,
                "Reinicio: intento incompleto queda sin confirmar y no se reenvía");
        }
        {
            var f = Fixture(); var transport = new ManualTransport(new("unknown", "Sin respuesta"));
            var invites = Invites(f, transport); var id = Guid.NewGuid().ToString();
            check(Status(await invites.SendAsync(new("person@example.com", 2, id), CancellationToken.None)) == 502 && invites.Receipts().Single().State == "unknown",
                "Respuesta perdida: conserva estado desconocido y devuelve referencia");
            check(Status(await invites.SendAsync(new("person@example.com", 2, id), CancellationToken.None)) == 502 && transport.Submissions == 1,
                "Respuesta perdida: repetir solicitud no duplica el correo");
            check(Status(await invites.SendAsync(new("person@example.com", 2), CancellationToken.None)) == 409 && transport.Submissions == 1,
                "Respuesta perdida: bloquea nueva invitación sin revisión explícita");
            transport.Result = new("accepted");
            check(Status(await invites.SendAsync(new("person@example.com", 2, ConfirmUncertain: true), CancellationToken.None)) == 200 && invites.Receipts().Length == 2,
                "Revisión manual: nueva invitación explícita conserva ambos comprobantes");
            check(Status(await invites.SendAsync(new("other@example.com", 2), CancellationToken.None)) == 200,
                "Revisión manual: una aceptación posterior resuelve el bloqueo de incertidumbre");
        }
        // Race cases: pause or slot occupancy can change while the mailbox is busy.
        foreach (var mode in new[] { "concurrent", "pause", "slot", "cancel" })
        {
            var f = Fixture(); var transport = new ManualTransport(new("accepted")) { WaitForRelease = true };
            var invites = Invites(f, transport);
            using var cancel = new CancellationTokenSource();
            var sending = invites.SendAsync(new("person@example.com", 2), cancel.Token);
            await transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (mode == "concurrent")
                check(Status(await invites.SendAsync(new("other@example.com", 2), CancellationToken.None)) == 409,
                    "Concurrencia: segunda solicitud se rechaza mientras la primera espera");
            if (mode == "pause") f.Box.CompleteBatch([], new("retry", "Límite", RetryAfterSeconds: 120, Throttled: true), DateTimeOffset.UtcNow);
            if (mode == "slot") Login(f.Access, 2);
            if (mode == "cancel") cancel.Cancel();
            transport.Release.TrySetResult();
            var result = await sending;
            check(mode == "concurrent" ? Status(result) == 200 && transport.Submissions == 1 :
                Status(result) == (mode == "slot" ? 409 : 503) && transport.Submissions == 0,
                "Revalidación: " + mode + " no produce un envío indebido");
        }
        {
            var f = Fixture(); var transport = new ManualTransport(new("retry", "Límite", RetryAfterSeconds: 120, Throttled: true, HttpStatus: 429));
            var invites = Invites(f, transport);
            await invites.SendAsync(new("person@example.com", 2), CancellationToken.None);
            check(f.Box.IsLimited(DateTimeOffset.UtcNow) && invites.Receipts().Single() is { State: "failed", HttpStatus: 429 },
                "429 manual: pausa compartida y comprobante de rechazo, sin reintento automático");
            check(Status(await invites.SendAsync(new("other@example.com", 3), CancellationToken.None)) == 503 && transport.Submissions == 1,
                "429 manual: siguiente invitación respeta la pausa");
        }
        // Symbolic errors only: no mail addresses or arbitrary provider messages in diagnostics.
        using (var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"error\":{\"code\":\"ErrorAccessDenied\",\"message\":\"private@example.com secret\"}}") })
            check(await EmailChannel.ReadErrorCodeAsync(response, CancellationToken.None) == "ErrorAccessDenied", "Graph: diagnóstico conserva solo código simbólico");
        foreach (var body in new[] { "invalid", "[]", "null", "{\"error\":{\"code\":\"secret@example.com\"}}", "{\"error\":null}", new string('x', 8193) })
        {
            using var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(body) };
            check(await EmailChannel.ReadErrorCodeAsync(response, CancellationToken.None) is null, "Graph: descarta diagnóstico inválido/privado/excesivo");
        }
        // A locked destination keeps its old contents and does not prevent other tables/events.
        {
            var f = Fixture(); string[][] oldRows = [["id", "name"], ["1", "Anterior"]]; string[][] newRows = [["id", "name"], ["2", "Nuevo"]];
            f.Events.Table("devices", oldRows); var target = Path.Combine(f.Events.DirectoryPath, "devices.csv");
            var old = File.ReadAllBytes(target);
            var worker = new EventJournalWorker(f.Events, new IncidentRepository(Path.Combine(f.Dir, "incidents.json")), f.Box, null!, NullLogger<EventJournalWorker>.Instance);
            using (var held = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                check(!worker.ExportTable("devices", newRows) && old.SequenceEqual(File.ReadAllBytes(target)),
                    "CSV: bloqueo persistente agota reintentos y conserva tabla anterior");
                check(worker.ExportTable("links", [["source", "target"], ["1", "2"]]), "CSV: una tabla bloqueada no impide exportar las demás");
                f.Events.Add("still-running", DateTimeOffset.UtcNow, "monitor", "info", "test", description: "still-running");
                check(File.ReadAllText(Path.Combine(f.Events.DirectoryPath, "events.csv")).Contains("still-running"), "CSV: eventos siguen registrándose después del fallo de tabla");
            }
            check(worker.ExportTable("devices", newRows) && File.ReadAllText(target).Contains("Nuevo") && !File.Exists(target + ".tmp"),
                "CSV: tras liberar bloqueo actualiza y limpia el temporal");
            using var transient = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
            var writing = Task.Run(() => f.Events.Table("devices", oldRows));
            await Task.Delay(90); transient.Dispose(); await writing;
            check(old.SequenceEqual(File.ReadAllBytes(target)), "CSV: bloqueo transitorio se recupera dentro del reintento acotado");
        }
    }

    sealed class ManualTransport(EmailDelivery result) : IManualEmailTransport
    {
        public EmailDelivery Result { get; set; } = result;
        public int Submissions { get; private set; }
        public Action? OnSubmit { get; set; }
        public bool WaitForRelease { get; init; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<EmailDelivery> SendDirectAsync(string recipient, string subject, string html, CancellationToken cancellation,
            Func<EmailDelivery?>? beforeSend = null, Action<EmailDelivery>? afterSend = null, string? requestId = null)
        {
            Entered.TrySetResult();
            if (WaitForRelease) await Release.Task.WaitAsync(cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (beforeSend?.Invoke() is { } blocked) return blocked;
            Submissions++; OnSubmit?.Invoke();
            afterSend?.Invoke(Result);
            return Result;
        }
    }
}
