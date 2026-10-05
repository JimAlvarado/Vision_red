public sealed record EmailDelivery(string State, string? Error = null, string? ProviderRequestId = null, int? RetryAfterSeconds = null);
public interface IAlertTransport
{
    bool AutomaticAlertsEnabled { get; }
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
                if (transport.AutomaticAlertsEnabled)
                {
                    var item = outbox.Claim(DateTimeOffset.UtcNow);
                    if (item is not null)
                    {
                        EmailDelivery result;
                        try { result = await transport.SendAlertAsync(item, stoppingToken); }
                        catch (Exception ex)
                        {
                            logger.LogWarning("El envío {Id} quedó sin confirmar ({Type}).", item.Id, ex.GetType().Name);
                            result = new("unknown", "No se pudo confirmar el envío. Revisar Elementos enviados antes de repetir.");
                        }
                        outbox.Complete(item.Id, result, DateTimeOffset.UtcNow);
                    }
                }
            }
            catch (Exception ex) { logger.LogError("No se pudo procesar la cola de correo ({Type}).", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
