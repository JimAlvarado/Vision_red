using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
var root = Path.Combine(Directory.GetCurrentDirectory(), "fixtures", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var sample = new Incident("subject", "127.0.0.1", "Prueba", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Prueba");
foreach (var kind in new[] { "down", "recovery" })
{
    if (!AlertMessage.Compose(sample, kind).Subject.StartsWith("[VISION-APODACA] ")) throw new Exception("Falta el prefijo para reglas de correo.");
    if (!AlertMessage.Compose(sample with { Ip = AlertMessage.AccessIp }, kind).Subject.StartsWith("[VISION-APODACA] ")) throw new Exception("Falta el prefijo en avisos de red.");
}
if (AlertMessage.Subject("[VISION-APODACA] Prueba") != "[VISION-APODACA] Prueba" ||
    !AlertMessage.Subject("Vision Apodaca | Aviso anterior").StartsWith("[VISION-APODACA] ")) throw new Exception("Normalización de asuntos pendientes incorrecta.");
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
var mailRoot = Path.Combine(root, "mail");
Directory.CreateDirectory(mailRoot);
File.WriteAllText(Path.Combine(mailRoot, "owner-notifications.json"), "{\"recipientAddress\":\"owner@example.invalid\",\"enabled\":true}");
var subjectsOutbox = new NotificationOutbox(Path.Combine(mailRoot, "subjects.json"));
var ownerNotices = new OwnerNotifications(mailRoot, subjectsOutbox);
ownerNotices.RecipientsAdded(["recipient@example.invalid"], now);
ownerNotices.MobileLogin("127.0.0.1", "Android", now);
if (subjectsOutbox.Snapshot().Length != 2 || subjectsOutbox.Snapshot().Any(n => !n.Subject.StartsWith("[VISION-APODACA] "))) throw new Exception("Falta el prefijo de avisos al autor.");
var mailSettings = new EmailSettings("microsoft-graph", "personal-device-code", Guid.NewGuid().ToString(),
    "sender@example.invalid", "recipient@example.invalid", "no-send", false);
foreach (var mode in new[] { "personal-device-code", "organizational-device-code" })
{
    File.WriteAllText(Path.Combine(mailRoot, "email-settings.json"), JsonSerializer.Serialize(mailSettings with { AuthMode = mode }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    using var channel = new EmailChannel(mailRoot, new Lifetime());
    if (await channel.CheckConnectionAsync()) throw new Exception("Una cuenta no autorizada no debe quedar conectada.");
}
File.WriteAllText(Path.Combine(mailRoot, "email-settings.json"), JsonSerializer.Serialize(mailSettings with { AuthMode = "organizational-device-code", TenantId = "../consumers" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
try { using var channel = new EmailChannel(mailRoot, new Lifetime()); throw new Exception("Se aceptó una organización inválida."); }
catch (InvalidDataException) { }
Console.WriteLine("Correcto: configuración personal e institucional, sin autorización implícita; organización inválida rechazada.");
sealed class FakeTransport : IAlertTransport
{
    public bool AutomaticAlertsEnabled=>false;
    public bool OwnerNotificationsEnabled=>true;
    public List<PendingNotification> Sent { get; }=[];
    public Task<EmailDelivery> SendAlertAsync(PendingNotification notification,CancellationToken token)
    { Sent.Add(notification);return Task.FromResult(new EmailDelivery("accepted")); }
}
sealed class Lifetime : IHostApplicationLifetime
{
    public CancellationToken ApplicationStarted => CancellationToken.None;
    public CancellationToken ApplicationStopping => CancellationToken.None;
    public CancellationToken ApplicationStopped => CancellationToken.None;
    public void StopApplication() { }
}
