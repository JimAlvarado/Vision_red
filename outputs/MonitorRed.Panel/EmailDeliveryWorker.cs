public sealed record EmailDelivery(string State, string? Error = null, string? ProviderRequestId = null, int? RetryAfterSeconds = null,
    bool Throttled = false, int? HttpStatus = null, string? ProviderErrorCode = null, string? ClientRequestId = null);
public interface IManualEmailTransport
{
    Task<EmailDelivery> SendDirectAsync(string recipient, string subject, string html, CancellationToken cancellation,
        Func<EmailDelivery?>? beforeSend = null, Action<EmailDelivery>? afterSend = null, string? requestId = null);
}
public interface IAlertTransport
{
    bool AutomaticAlertsEnabled { get; }
    bool OwnerNotificationsEnabled => false;
    Task<EmailDelivery> SendAlertAsync(PendingNotification notification, CancellationToken token);
}
public sealed class EmailDeliveryWorker(NotificationOutbox outbox, IAlertTransport transport,
    ILogger<EmailDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (transport.AutomaticAlertsEnabled || transport.OwnerNotificationsEnabled)
                {
                    var now = DateTimeOffset.UtcNow;
                    var batch = outbox.ClaimBatch(now, transport.AutomaticAlertsEnabled, transport.OwnerNotificationsEnabled);
                    if (batch.Length > 0)
                    {
                        // One network change from a stable device keeps its own message; anything else becomes a summary.
                        var single = batch.Length == 1 && (batch[0].OwnerOnly || !outbox.IsIntermittent(batch[0].Ip, now));
                        var message = single ? batch[0] : AlertMessage.ComposeDigest(batch, outbox, now);
                        EmailDelivery result;
                        try { result = await transport.SendAlertAsync(message, stoppingToken); }
                        catch (Exception ex)
                        {
                            logger.LogWarning("El envío {Id} quedó sin confirmar ({Type}).", message.Id, ex.GetType().Name);
                            result = new("unknown", "No se pudo confirmar el envío. Revisar Elementos enviados antes de repetir.");
                        }
                        outbox.CompleteBatch(batch.Select(i => i.Id).ToArray(), result, DateTimeOffset.UtcNow);
                    }
                }
            }
            catch (Exception ex) { logger.LogError("No se pudo procesar la cola de correo ({Type}).", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
