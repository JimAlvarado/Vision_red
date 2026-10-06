using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Identity.Client;

public sealed record EmailSettings(string Provider, string AuthMode, string ClientId, string SenderAddress,
    string TestRecipient, string TestRequestId, bool AutomaticAlertsEnabled, string[]? Recipients = null,
    string? TenantId = null);
public sealed record EmailAuthorization(string State, string? UserCode = null, string? VerificationUrl = null,
    DateTimeOffset? ExpiresAtUtc = null, string? Error = null);
public sealed record EmailReceipt(string RequestId, string Recipient, string Subject, string State,
    DateTimeOffset UpdatedAtUtc, string? Error = null, string? ProviderRequestId = null);

public sealed class EmailChannel : IDisposable, IAlertTransport
{
    private static readonly string[] Scopes = ["https://graph.microsoft.com/Mail.Send"];
    private volatile EmailSettings settings;
    private readonly string settingsPath;
    private readonly OwnerNotifications? owner;
    private readonly IPublicClientApplication identity;
    private readonly HttpClient http;
    private readonly string cachePath;
    private readonly string receiptPath;
    private readonly object cacheGate = new();
    private readonly object authGate = new();
    private readonly SemaphoreSlim sendGate = new(1);
    private readonly CancellationToken stopping;
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private EmailAuthorization authorization = new("disconnected");
    private Task? authorizationTask;
    private bool forceRefresh;

    public EmailChannel(string dataDir, IHostApplicationLifetime lifetime, OwnerNotifications? owner = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("El correo de Vision requiere Windows.");
        this.owner = owner;
        settingsPath = Path.Combine(dataDir, "email-settings.json");
        settings = JsonSerializer.Deserialize<EmailSettings>(File.ReadAllText(settingsPath), json)
            ?? throw new InvalidDataException("Configuración de correo vacía.");
        if (settings.Provider != "microsoft-graph" || settings.AuthMode is not ("personal-device-code" or "organizational-device-code") ||
            !Guid.TryParse(settings.ClientId, out _))
            throw new InvalidDataException("Configura Microsoft Graph con acceso personal o institucional y un Id. de cliente válido.");
        var tenant = settings.AuthMode == "personal-device-code" ? "consumers" : settings.TenantId ?? "organizations";
        if (settings.AuthMode == "organizational-device-code" && tenant != "organizations" && !Guid.TryParse(tenant, out _))
            throw new InvalidDataException("El identificador de la organización debe ser un GUID o organizations.");
        _ = new System.Net.Mail.MailAddress(settings.SenderAddress);
        _ = new System.Net.Mail.MailAddress(settings.TestRecipient);
        foreach (var recipient in settings.Recipients ?? [settings.TestRecipient]) _ = new System.Net.Mail.MailAddress(recipient);
        if (settings.Recipients is { Length: 0 }) throw new InvalidDataException("Faltan destinatarios de alertas.");
        cachePath = Path.Combine(dataDir, "email-session.dpapi");
        receiptPath = Path.Combine(dataDir, "email-test-receipt.json");
        stopping = lifetime.ApplicationStopping;
        identity = PublicClientApplicationBuilder.Create(settings.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{tenant}")
            .Build();
        identity.UserTokenCache.SetBeforeAccess(args =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            lock (cacheGate)
            {
                if (!File.Exists(cachePath)) return;
                var encrypted = File.ReadAllBytes(cachePath);
                var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                try { args.TokenCache.DeserializeMsalV3(plain); }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
        });
        identity.UserTokenCache.SetAfterAccess(args =>
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            if (!args.HasStateChanged) return;
            lock (cacheGate)
            {
                var plain = args.TokenCache.SerializeMsalV3();
                try { SaveBytes(cachePath, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser)); }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
        });
        http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(35) };
        FlushOwnerChanges(JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject());
    }

    public bool AutomaticAlertsEnabled => settings.AutomaticAlertsEnabled;
    public bool OwnerNotificationsEnabled => owner?.Enabled == true;
    public string[] Recipients => (settings.Recipients ?? [settings.TestRecipient])
        .Concat(OwnerNotificationsEnabled ? [owner!.Address!] : Array.Empty<string>())
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<string[]> UpdateRecipientsAsync(string[]? addresses, CancellationToken cancellation)
    {
        if (addresses is null || addresses.Length is < 1 or > 50)
            throw new ArgumentException("Agrega entre 1 y 50 destinatarios.");
        var recipients = new List<string>();
        foreach (var value in addresses)
        {
            var address = value?.Trim() ?? "";
            if (!IsValidAddress(address))
                throw new ArgumentException("Hay una dirección de correo inválida. Revisa la lista.");
            if (!recipients.Contains(address, StringComparer.OrdinalIgnoreCase)) recipients.Add(address);
        }
        if (OwnerNotificationsEnabled && !recipients.Contains(owner!.Address!, StringComparer.OrdinalIgnoreCase)) recipients.Add(owner.Address!);
        if (recipients.Count > 50) throw new ArgumentException("Reserva un lugar para el correo del autor; el máximo es 50 destinatarios.");
        await sendGate.WaitAsync(cancellation);
        try
        {
            var document = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject
                ?? throw new InvalidDataException("Configuración de correo inválida.");
            document["recipients"] = JsonSerializer.SerializeToNode(recipients.ToArray());
            var added = recipients.Except(Recipients, StringComparer.OrdinalIgnoreCase).ToArray();
            if (OwnerNotificationsEnabled && added.Length > 0)
            {
                var pending = document["ownerNotificationPending"] as JsonArray;
                if (pending is null) { pending = new JsonArray(); document["ownerNotificationPending"] = pending; }
                pending.Add(JsonSerializer.SerializeToNode(new { id = Guid.NewGuid().ToString("N"), addresses = added, occurredAtUtc = DateTimeOffset.UtcNow }));
            }
            SaveBytes(settingsPath, JsonSerializer.SerializeToUtf8Bytes(document, json));
            settings = settings with { Recipients = recipients.ToArray() };
            FlushOwnerChanges(document);
            return Recipients;
        }
        finally { sendGate.Release(); }
    }

    // A plain address with a dotted domain such as empresa.com; rejects "usuario@empresa" and display names.
    public static bool IsValidAddress(string address) =>
        address.Length is >= 3 and <= 254 && !address.Any(char.IsControl) && address.Contains('@') &&
        System.Net.Mail.MailAddress.TryCreate(address, out var parsed) &&
        string.Equals(parsed.Address, address, StringComparison.OrdinalIgnoreCase) && ValidDomain(parsed.Host);

    private static bool ValidDomain(string host) =>
        host.Contains('.') && !host.StartsWith('.') && !host.EndsWith('.') && !host.Contains("..");

    public async Task SetAutomaticAlertsAsync(bool enabled, CancellationToken cancellation)
    {
        await sendGate.WaitAsync(cancellation);
        try
        {
            var document = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject
                ?? throw new InvalidDataException("Configuración de correo inválida.");
            document["automaticAlertsEnabled"] = enabled;
            SaveBytes(settingsPath, JsonSerializer.SerializeToUtf8Bytes(document, json));
            settings = settings with { AutomaticAlertsEnabled = enabled };
        }
        finally { sendGate.Release(); }
    }

    private void FlushOwnerChanges(JsonObject document)
    {
        if (!OwnerNotificationsEnabled || document["ownerNotificationPending"] is not JsonArray pending || pending.Count == 0) return;
        foreach (var item in pending)
        {
            var change = item!.AsObject();
            owner!.RecipientsAdded(change["addresses"]!.Deserialize<string[]>()!,
                change["occurredAtUtc"]!.Deserialize<DateTimeOffset>(), change["id"]!.GetValue<string>());
        }
        document.Remove("ownerNotificationPending");
        SaveBytes(settingsPath, JsonSerializer.SerializeToUtf8Bytes(document, json));
    }

    public async Task<bool> CheckConnectionAsync()
    {
        try { _ = await AcquireTokenAsync(stopping); return true; }
        catch (Exception ex) when (ex is MsalException or InvalidOperationException or HttpRequestException or CryptographicException)
        { return false; }
    }

    private async Task<AuthenticationResult> AcquireTokenAsync(CancellationToken cancellation)
    {
        var account = (await identity.GetAccountsAsync()).FirstOrDefault(IsExpectedAccount);
        if (account is null) throw new InvalidOperationException("Primero autoriza el buzón de Vision.");
        try
        {
            var token = await identity.AcquireTokenSilent(Scopes, account).WithForceRefresh(forceRefresh).ExecuteAsync(cancellation);
            forceRefresh = false;
            if (!IsExpectedAccount(token.Account)) throw new InvalidOperationException("El buzón autorizado no coincide.");
            lock (authGate) authorization = new("connected");
            return token;
        }
        catch (MsalException ex)
        {
            lock (authGate) authorization = new("error", Error: AuthError(ex.ErrorCode));
            throw;
        }
    }

    public async Task<EmailDelivery> SendAlertAsync(PendingNotification notification, CancellationToken cancellation)
    {
        if (!(notification.OwnerOnly ? OwnerNotificationsEnabled : AutomaticAlertsEnabled))
            return new("retry", "Este canal de avisos está desactivado.", RetryAfterSeconds: 60);
        await sendGate.WaitAsync(cancellation);
        try
        {
            var recipients = notification.OwnerOnly ? notification.TargetRecipients ?? [] : Recipients;
            if (recipients.Length == 0 || notification.OwnerOnly && recipients.Length != 1)
                return new("failed", "Falta el destinatario exclusivo del aviso al autor.");
            AuthenticationResult token;
            try { token = await AcquireTokenAsync(cancellation); }
            catch (Exception ex) when (ex is MsalException or InvalidOperationException or HttpRequestException or CryptographicException)
            { return new("retry", "No se pudo autorizar el envío. Consultar la conexión de correo en Vision.", RetryAfterSeconds: 60); }
            return await PostMailAsync(token, notification.Subject, notification.Message, recipients, cancellation);
        }
        finally { sendGate.Release(); }
    }

    // Manual messages from the local editor (mobile invitations); independent of the automatic channels.
    public async Task<EmailDelivery> SendDirectAsync(string recipient, string subject, string html, CancellationToken cancellation)
    {
        await sendGate.WaitAsync(cancellation);
        try
        {
            AuthenticationResult token;
            try { token = await AcquireTokenAsync(cancellation); }
            catch (Exception ex) when (ex is MsalException or InvalidOperationException or HttpRequestException or CryptographicException)
            { return new("failed", "El buzón de Vision no está autorizado. Revisa Configuración → Correo."); }
            return await PostMailAsync(token, subject, html, [recipient], cancellation);
        }
        finally { sendGate.Release(); }
    }

    private async Task<EmailDelivery> PostMailAsync(AuthenticationResult token, string subject, string html, string[] recipients, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/sendMail");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        request.Headers.Add("client-request-id", Guid.NewGuid().ToString());
        request.Content = JsonContent.Create(new
        {
            message = new
            {
                subject = AlertMessage.Subject(subject),
                body = new { contentType = "HTML", content = html },
                from = new { emailAddress = new { address = settings.SenderAddress } },
                toRecipients = recipients.Select(address => new { emailAddress = new { address } }).ToArray()
            },
            saveToSentItems = true
        });
        try
        {
            using var response = await http.SendAsync(request, cancellation);
            var providerId = response.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null;
            if (response.StatusCode == HttpStatusCode.Accepted) return new("accepted", ProviderRequestId: providerId);
            if ((int)response.StatusCode == 429)
            {
                var after = response.Headers.RetryAfter?.Delta?.TotalSeconds ??
                    (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 60;
                return new("retry", "Microsoft limitó temporalmente el envío.", providerId, (int)Math.Clamp(after, 5, 3600), Throttled: true);
            }
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                forceRefresh = true;
                lock (authGate) authorization = new("error", Error: "Microsoft rechazó la sesión. Vuelve a autorizar el buzón.");
                return new("retry", "Microsoft rechazó la sesión de correo.", providerId, 60);
            }
            return new((int)response.StatusCode >= 500 ? "unknown" : "failed",
                $"Microsoft devolvió HTTP {(int)response.StatusCode}." + ((int)response.StatusCode >= 500 ? " Revisar Elementos enviados antes de repetir." : ""), providerId);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        { return new("unknown", "No se recibió confirmación del envío. Revisar Elementos enviados antes de repetir."); }
    }

    public async Task<object> StatusAsync(SendingLimit? limit = null)
    {
        EmailAuthorization auth;
        lock (authGate) auth = authorization;
        try
        {
            var account = (await identity.GetAccountsAsync()).FirstOrDefault(IsExpectedAccount);
            if (account is not null && auth.State == "disconnected") auth = new("connected");
        }
        catch (CryptographicException) { auth = new("error", Error: "La sesión guardada pertenece a otro usuario de Windows o no se puede abrir. Vuelve a autorizar."); }
        return new { settings.Provider, settings.AuthMode, settings.SenderAddress, settings.TestRecipient,
            settings.AutomaticAlertsEnabled, recipients = Recipients, ownerNotificationsEnabled = OwnerNotificationsEnabled,
            ownerNotificationAddress = owner?.Address, ownerConfigurationError = owner?.ConfigurationError,
            limitedUntilUtc = limit is { PausedUntilUtc: { } until } && until > DateTimeOffset.UtcNow ? until : (DateTimeOffset?)null,
            authorization = auth, test = ReadReceipt() };
    }

    public async Task<EmailAuthorization> StartAuthorizationAsync()
    {
        TaskCompletionSource<EmailAuthorization>? published = null;
        lock (authGate)
        {
            if (authorizationTask is { IsCompleted: false }) return authorization;
            published = new(TaskCreationOptions.RunContinuationsAsynchronously);
            authorization = new("starting");
            authorizationTask = Task.Run(() => AuthorizeAsync(published), stopping);
        }
        try { return await published.Task.WaitAsync(TimeSpan.FromSeconds(20), stopping); }
        catch (TimeoutException) { lock (authGate) return authorization; }
    }

    private async Task AuthorizeAsync(TaskCompletionSource<EmailAuthorization> published)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(TimeSpan.FromMinutes(16));
        try
        {
            var result = await identity.AcquireTokenWithDeviceCode(Scopes, code =>
            {
                var next = new EmailAuthorization("waiting", code.UserCode, code.VerificationUrl, code.ExpiresOn);
                lock (authGate) authorization = next;
                published.TrySetResult(next);
                return Task.CompletedTask;
            }).ExecuteAsync(timeout.Token);
            if (!IsExpectedAccount(result.Account))
            {
                await identity.RemoveAsync(result.Account);
                lock (authGate) authorization = new("error", Error: $"Autoriza con {settings.SenderAddress}; la cuenta elegida no coincide.");
            }
            else
            {
                lock (authGate) authorization = new("connected");
            }
        }
        catch (MsalException ex)
        {
            lock (authGate) authorization = new("error", Error: AuthError(ex.ErrorCode));
        }
        catch (OperationCanceledException)
        {
            lock (authGate) authorization = new("error", Error: "La autorización venció o Vision se detuvo. Solicita un nuevo código.");
        }
        catch (Exception)
        {
            lock (authGate) authorization = new("error", Error: "No se pudo guardar o completar la autorización. Revisa la conexión y los permisos locales.");
        }
        finally { lock (authGate) published.TrySetResult(authorization); }
    }

    public async Task<EmailReceipt> SendTestAsync()
    {
        await sendGate.WaitAsync(stopping);
        try
        {
            var previous = ReadReceipt();
            // A lost response or restart after submission must never silently resend this test.
            if (previous?.State is "accepted" or "sending" or "unknown") return previous;
            var account = (await identity.GetAccountsAsync()).FirstOrDefault(IsExpectedAccount);
            if (account is null) throw new InvalidOperationException("Primero autoriza el buzón de Vision.");
            AuthenticationResult token;
            try { token = await identity.AcquireTokenSilent(Scopes, account).ExecuteAsync(stopping); }
            catch (MsalException ex)
            {
                lock (authGate) authorization = new("error", Error: AuthError(ex.ErrorCode));
                throw new InvalidOperationException("Microsoft requiere volver a autorizar el buzón.");
            }
            if (!IsExpectedAccount(token.Account)) throw new InvalidOperationException("El buzón autorizado no coincide.");
            var subject = AlertMessage.Subject("Prueba de correo");
            var receipt = new EmailReceipt(settings.TestRequestId, settings.TestRecipient, subject, "sending", DateTimeOffset.UtcNow);
            SaveReceipt(receipt);
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/sendMail");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
            request.Headers.Add("client-request-id", Guid.NewGuid().ToString());
            request.Content = JsonContent.Create(new
            {
                message = new
                {
                    subject,
                    body = new { contentType = "Text", content =
                        $"Este es un correo de prueba de Vision Apodaca.\n\nRemitente: {settings.SenderAddress}\n" +
                        $"Fecha: {AlertMessage.FormatTime(DateTimeOffset.UtcNow)}\nReferencia: {settings.TestRequestId}\n\n" +
                        "Esta prueba verifica la conexión de Vision con el correo. No representa una caída de equipos.\n" +
                        "Esta prueba no modifica la configuración de alertas automáticas.\n\nVision | Monitoreo de red" },
                    from = new { emailAddress = new { address = settings.SenderAddress } },
                    toRecipients = new[] { new { emailAddress = new { address = settings.TestRecipient } } }
                },
                saveToSentItems = true
            });
            try
            {
                using var response = await http.SendAsync(request, stopping);
                var providerId = response.Headers.TryGetValues("request-id", out var ids) ? ids.FirstOrDefault() : null;
                if (response.StatusCode == HttpStatusCode.Accepted)
                    receipt = receipt with { State = "accepted", UpdatedAtUtc = DateTimeOffset.UtcNow, ProviderRequestId = providerId };
                else
                {
                    // Server errors have an ambiguous outcome; no automatic retries for this manual test.
                    receipt = receipt with { State = (int)response.StatusCode >= 500 ? "unknown" : "failed",
                        UpdatedAtUtc = DateTimeOffset.UtcNow, Error = $"Microsoft devolvió HTTP {(int)response.StatusCode}.", ProviderRequestId = providerId };
                }
            }
            catch (HttpRequestException)
            { receipt = receipt with { State = "unknown", UpdatedAtUtc = DateTimeOffset.UtcNow, Error = "Se perdió la conexión. Revisa Elementos enviados antes de repetir." }; }
            catch (OperationCanceledException)
            { receipt = receipt with { State = "unknown", UpdatedAtUtc = DateTimeOffset.UtcNow, Error = "No se recibió confirmación. Revisa Elementos enviados antes de repetir." }; }
            SaveReceipt(receipt);
            return receipt;
        }
        finally { sendGate.Release(); }
    }

    private bool IsExpectedAccount(IAccount account) =>
        string.Equals(account.Username, settings.SenderAddress, StringComparison.OrdinalIgnoreCase);
    private static string AuthError(string code) => code switch
    {
        "authorization_declined" => "Se rechazó la autorización. Solicita un nuevo código.",
        "expired_token" => "El código venció. Solicita uno nuevo.",
        "invalid_client" or "unauthorized_client" => "Revisa el Id. de cliente, los tipos de cuenta y los flujos de cliente público en Entra.",
        "invalid_scope" => "Revisa el permiso delegado Mail.Send en Entra.",
        _ => "Microsoft no pudo autorizar el buzón. Revisa la conexión, Mail.Send y los flujos de cliente público. Código: " +
            new string(code.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').Take(80).ToArray())
    };
    private EmailReceipt? ReadReceipt() => File.Exists(receiptPath)
        ? JsonSerializer.Deserialize<EmailReceipt>(File.ReadAllText(receiptPath), json) : null;
    private void SaveReceipt(EmailReceipt value) => SaveBytes(receiptPath, JsonSerializer.SerializeToUtf8Bytes(value, json));
    private static void SaveBytes(string path, byte[] bytes)
    {
        File.WriteAllBytes(path + ".tmp", bytes);
        File.Move(path + ".tmp", path, true);
    }
    public void Dispose() { http.Dispose(); }
}
