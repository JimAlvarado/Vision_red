using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;

public sealed record VisionEvent(string Id, DateTimeOffset OccurredAtUtc, string Category, string Severity,
    string Kind, string DeviceIp, string DeviceName, string PreviousState, string CurrentState,
    string ReferenceId, string Description);
public sealed class EventRepository
{
    private readonly object gate = new();
    private readonly List<VisionEvent> items = [];
    private readonly HashSet<string> ids = [];
    private readonly string path;
    public string DirectoryPath { get; }
    public EventRepository(string dataDir)
    {
        DirectoryPath = Path.Combine(dataDir, "csv"); Directory.CreateDirectory(DirectoryPath);
        path = Path.Combine(DirectoryPath, "events.csv");
        if (!File.Exists(path)) File.WriteAllText(path, CsvRow(["id","occurred_at_utc","category","severity","kind","device_ip","device_name","previous_state","current_state","reference_id","description"]), new UTF8Encoding(false));
        using var parser = new TextFieldParser(path, Encoding.UTF8) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(","); parser.ReadFields();
        while (!parser.EndOfData)
        {
            var r = parser.ReadFields()!;
            if (r.Length != 11) throw new InvalidDataException("La tabla de eventos tiene una fila inválida. Conservar el archivo y revisar el respaldo.");
            var item = new VisionEvent(r[0], DateTimeOffset.Parse(r[1], CultureInfo.InvariantCulture),r[2],r[3],r[4],r[5],r[6],r[7],r[8],r[9],r[10]);
            if (ids.Add(item.Id)) items.Add(item);
        }
    }
    public static string StableId(string key) => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0,16)).ToString();
    public void Add(string key, DateTimeOffset time, string category, string severity, string kind,
        string ip = "", string name = "", string from = "", string to = "", string reference = "", string description = "")
    {
        var item = new VisionEvent(StableId(key),time.ToUniversalTime(),category,severity,kind,ip,name,from,to,reference,description);
        lock (gate)
        {
            if (ids.Contains(item.Id)) return;
            var row = CsvRow([item.Id,item.OccurredAtUtc.ToString("O",CultureInfo.InvariantCulture),category,severity,kind,ip,name,from,to,reference,description]);
            using (var stream = new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read))
            { var bytes = Encoding.UTF8.GetBytes(row);stream.Write(bytes);stream.Flush(true); }
            ids.Add(item.Id);items.Add(item);
        }
    }
    public object Query(string? search, string? category, string? severity, DateTimeOffset? since, DateTimeOffset? until, int page = 1)
    {
        lock (gate)
        {
            IEnumerable<VisionEvent> selected = items;
            if (!string.IsNullOrWhiteSpace(search)) selected=System.Net.IPAddress.TryParse(search,out _)
                ? selected.Where(i=>i.DeviceIp==search)
                : selected.Where(i=>$"{i.DeviceName} {i.DeviceIp} {i.Description} {i.ReferenceId}".Contains(search,StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(category)) selected=selected.Where(i=>i.Category==category);
            if (!string.IsNullOrEmpty(severity)) selected=selected.Where(i=>i.Severity==severity);
            if (since is not null) selected=selected.Where(i=>i.OccurredAtUtc>=since);
            if (until is not null) selected=selected.Where(i=>i.OccurredAtUtc<=until);
            var rows=selected.OrderByDescending(i=>i.OccurredAtUtc).ThenBy(i=>i.Id).ToArray();
            page=Math.Max(1,page);return new { total=rows.Length,page,pageSize=100,items=rows.Skip((int)Math.Min((long)(page-1)*100,int.MaxValue)).Take(100).ToArray() };
        }
    }
    public void Incident(Incident item)
    {
        var network=item.Ip==AlertMessage.AccessIp;
        Add($"incident:{item.Id}:down",item.OpenedAtUtc,"incident","critical",network?"network_down":"device_down",item.Ip,item.Name,"online","offline",item.Id,"Pérdida de respuesta confirmada por el monitor.");
        if(item.ClosedAtUtc is { } closed)Add($"incident:{item.Id}:recovery",closed,"incident","success",network?"network_recovery":"device_recovery",item.Ip,item.Name,"offline","online",item.Id,$"Conectividad restablecida. Duración registrada: {Math.Round((closed-item.OpenedAtUtc).TotalSeconds)} segundos.");
    }
    public void Notification(PendingNotification item)
    {
        var time=item.AcceptedAtUtc ?? (item.Attempts==0?item.CreatedAtUtc:DateTimeOffset.UtcNow);
        Add($"email:{item.Id}:{item.Attempts}:{item.Status}",time,"email",item.Status=="accepted"?"success":item.Status is "failed" or "unknown" or "expired"?"warning":"info","email_"+item.Status,item.Ip,item.Name,"",item.Status,item.IncidentId,
            $"Aviso de {(item.Kind switch { "down" => "pérdida", "recovery" => "recuperación", "mobile_login" => "acceso a la consulta", "recipient_added" => "nuevo destinatario", _ => "correo" })}. Intento {item.Attempts}. {item.LastError ?? (item.Status=="accepted"?"Microsoft aceptó el envío; no equivale a confirmación de entrega.":"Estado de la cola de correo.")}");
    }
    public static string CsvRow(IEnumerable<string?> values) => string.Join(",",values.Select(v=>"\""+(v??"").Replace("\"","\"\"")+"\""))+"\r\n";
    public void Table(string name, IEnumerable<IEnumerable<string?>> rows)
    {
        var content=string.Concat(rows.Select(CsvRow));var target=Path.Combine(DirectoryPath,name+".csv");
        lock(gate) { if(File.Exists(target)&&File.ReadAllText(target)==content)return;File.WriteAllText(target+".tmp",content,new UTF8Encoding(false));File.Move(target+".tmp",target,true); }
    }
}
