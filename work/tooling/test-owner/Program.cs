using Microsoft.Extensions.Logging.Abstractions;
var root = Path.Combine(Directory.GetCurrentDirectory(), "fixtures", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var now = DateTimeOffset.UtcNow;
var outbox = new NotificationOutbox(Path.Combine(root,"queue.json"));
outbox.Enqueue(new Incident("network", "127.0.0.1", "Prueba", now, null, "Prueba"),"down");
outbox.EnqueueOwner("owner","mobile_login","127.0.0.1","Prueba",now,"Prueba","Prueba","autor@example.invalid");
var fake = new FakeTransport();
var worker = new EmailDeliveryWorker(outbox,fake,NullLogger<EmailDeliveryWorker>.Instance);
await worker.StartAsync(CancellationToken.None);
for(var i=0;i<100 && !outbox.Snapshot().Any(n=>n.Id=="owner" && n.Status=="accepted");i++)await Task.Delay(10);
await worker.StopAsync(CancellationToken.None);
if(fake.Sent.Count!=1 || fake.Sent[0].Id!="owner" || fake.Sent[0].TargetRecipients is not ["autor@example.invalid"])
    throw new Exception("El aviso al autor debe procesarse con alertas de red desactivadas y destinatario exclusivo.");
if(outbox.Snapshot().Single(n=>n.IncidentId=="network").Status!="awaiting-configuration")throw new Exception("Se procesó una alerta de red desactivada.");
var stale=new NotificationOutbox(Path.Combine(root,"stale.json"));
stale.Enqueue(new Incident("old-network","127.0.0.1","Prueba",now.AddHours(-2),null,"Prueba"),"down");
stale.EnqueueOwner("old-owner","mobile_login","127.0.0.1","Prueba",now.AddHours(-2),"Prueba","Prueba","autor@example.invalid");
if(stale.Claim(now,false,true)?.Id!="old-owner" || stale.Snapshot().Single(n=>n.IncidentId=="old-network").Status!="expired")throw new Exception("Caducidad de canales incorrecta.");
Console.WriteLine("Correcto: canal de autor independiente, destino exclusivo, red desactivada intacta y caducidad diferenciada. Transporte ficticio, sin correo real.");
sealed class FakeTransport : IAlertTransport
{
    public bool AutomaticAlertsEnabled=>false;
    public bool OwnerNotificationsEnabled=>true;
    public List<PendingNotification> Sent { get; }=[];
    public Task<EmailDelivery> SendAlertAsync(PendingNotification notification,CancellationToken token)
    { Sent.Add(notification);return Task.FromResult(new EmailDelivery("accepted")); }
}
