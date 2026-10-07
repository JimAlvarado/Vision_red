using System.Text.Json;

public sealed class EventJournalWorker(EventRepository events, IncidentRepository incidents, NotificationOutbox notifications,
    IWebHostEnvironment environment, ILogger<EventJournalWorker> logger) : BackgroundService
{
    private readonly Dictionary<string,long> lengths=[];
    public bool ExportTable(string name, IEnumerable<IEnumerable<string?>> rows)
    {
        try { events.Table(name, rows); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("No se pudo actualizar {Table}.csv ({Type}, 0x{Code:X8}). Se conserva la tabla anterior y se reintentará en el próximo ciclo.",
                name, ex.InnerException?.GetType().Name ?? ex.GetType().Name, ex.InnerException?.HResult ?? ex.HResult);
            return false;
        }
    }
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var dataDir=Path.Combine(environment.ContentRootPath,"datos");
        events.Add(Guid.NewGuid().ToString(),DateTimeOffset.UtcNow,"system","info","monitor_started",description:"Vision inició el monitoreo y el registro CSV.");
        while(!token.IsCancellationRequested)
        {
            try
            {
                foreach(var path in Directory.EnumerateFiles(dataDir,"eventos-monitor-*.jsonl"))
                {
                    var length=new FileInfo(path).Length;if(lengths.GetValueOrDefault(path,-1)==length)continue;
                    foreach(var line in JournalFile.ReadLines(path))
                    {
                        if(string.IsNullOrWhiteSpace(line))continue;
                        try
                        {
                            using var doc=JsonDocument.Parse(line);var r=doc.RootElement;
                            var to=r.GetProperty("to").GetString()??"";
                            var from=r.GetProperty("from").GetString()??"";
                            events.Add("monitor:"+line,r.GetProperty("timeUtc").GetDateTimeOffset(),"monitor",to is "offline" or "unreachable"?"warning":to=="online"?"success":"info","state_changed",r.GetProperty("ip").GetString()??"",r.GetProperty("name").GetString()??"",from,to,description:r.GetProperty("detail").GetString()??"");
                        }
                        catch(JsonException){logger.LogWarning("Una línea incompleta del registro del monitor será revisada en el siguiente ciclo.");}
                    }
                    lengths[path]=length;
                }
                var allIncidents=incidents.Snapshot();foreach(var item in allIncidents)events.Incident(item);
                var allNotifications=notifications.Snapshot();foreach(var item in allNotifications)events.Notification(item);
                ExportTable("incidents",new[]{new[]{"id","device_ip","device_name","opened_at_utc","closed_at_utc","detail"}}.Concat(allIncidents.Select(i=>new[]{i.Id,i.Ip,i.Name,i.OpenedAtUtc.ToUniversalTime().ToString("O"),i.ClosedAtUtc?.ToUniversalTime().ToString("O")??"",i.Detail})));
                ExportTable("email_notifications",new[]{new[]{"id","incident_id","device_ip","device_name","kind","status","attempts","created_at_utc","accepted_at_utc","provider_request_id","last_error"}}.Concat(allNotifications.Select(i=>new[]{i.Id,i.IncidentId,i.Ip,i.Name,i.Kind,i.Status,i.Attempts.ToString(),i.CreatedAtUtc.ToUniversalTime().ToString("O"),i.AcceptedAtUtc?.ToUniversalTime().ToString("O")??"",i.ProviderRequestId??"",i.LastError??""})));
                using var topology=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(dataDir,"topologia.json"),token));
                var devices=topology.RootElement.GetProperty("devices").EnumerateArray();
                ExportTable("devices",new[]{new[]{"id","name","ip","type","critical","x","y","note"}}.Concat(devices.Select(d=>new[]{d.GetProperty("id").GetString()!,d.GetProperty("name").GetString()!,d.GetProperty("ip").GetString()!,d.GetProperty("type").GetString()!,d.GetProperty("critical").ToString().ToLowerInvariant(),d.GetProperty("x").ToString(),d.GetProperty("y").ToString(),d.GetProperty("note").GetString()??""})));
                ExportTable("links",new[]{new[]{"source_device_id","target_device_id"}}.Concat(topology.RootElement.GetProperty("edges").EnumerateArray().Select(e=>new[]{e.GetProperty("source").GetString()!,e.GetProperty("target").GetString()!})));
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch(Exception ex){logger.LogError(ex,"No se pudo actualizar el registro CSV de eventos.");}
            try{await Task.Delay(5000,token);}catch(OperationCanceledException){break;}
        }
    }
}
