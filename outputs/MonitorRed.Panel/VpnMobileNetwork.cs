using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

// allowedSubnets lists every private network that may open the mobile view (VPN, VLANs);
// allowedSubnet is the original single-network format and is still accepted.
public sealed record VpnMobileSettings(string VpnAddress, string? AllowedSubnet = null, string[]? AllowedSubnets = null);
public sealed class VpnMobileNetwork
{
    public const int MaxSubnets = 20;
    private sealed record Networks(string[] Names, (uint Network, uint Mask)[] Ranges);
    private Networks networks;
    private readonly object settingsGate = new();
    private readonly string settingsPath;
    public string VpnAddress { get; }
    public bool ListeningOnVpn { get; }
    public IReadOnlyList<string> AllowedSubnets => Array.AsReadOnly(Volatile.Read(ref networks).Names);
    public string Url => $"http://{VpnAddress}:5081/";
    public VpnMobileNetwork(string dataDir)
    {
        settingsPath = Path.Combine(dataDir, "mobile-vpn-settings.json");
        var settings = JsonSerializer.Deserialize<VpnMobileSettings>(File.ReadAllText(settingsPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Falta la configuración VPN móvil.");
        var address = IPAddress.Parse(settings.VpnAddress);
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || !IsPrivate(address))
            throw new InvalidDataException("El acceso móvil debe permanecer en una red IPv4 privada.");
        var configured = (settings.AllowedSubnets is { Length: > 0 } many ? many : settings.AllowedSubnet is { } one ? [one] : [])
            .Select(s => s.Trim()).Distinct().ToArray();
        if (configured.Length is 0 or > MaxSubnets) throw new InvalidDataException($"Configura entre 1 y {MaxSubnets} redes autorizadas para el acceso móvil.");
        networks = new(configured, configured.Select(Parse).ToArray());
        VpnAddress = address.ToString();
        ListeningOnVpn = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Any(a => a.Address.Equals(address));
    }
    public string[] UpdateAllowedSubnets(string[]? requested, IMobileFirewall firewall)
    {
        if (requested is null || requested.Length is < 1 or > MaxSubnets || requested.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"Agrega entre 1 y {MaxSubnets} rangos IPv4 privados.");
        // Validate the complete list before touching Windows, disk, or the active access policy.
        var names = requested.Select(s => s.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        Networks next;
        try { next = new(names, names.Select(Parse).ToArray()); }
        catch (InvalidDataException ex) { throw new ArgumentException(ex.Message); }
        lock (settingsGate)
        {
            var temporary = settingsPath + ".tmp";
            try
            {
                var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject()
                    ?? throw new InvalidDataException("No se pudo leer la configuración de acceso móvil.");
                document["allowedSubnets"] = JsonSerializer.SerializeToNode(names);
                document.Remove("allowedSubnet");
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    stream.Write(bytes); stream.Flush(true);
                }
                // The old firewall scope is restored if saving the configuration fails.
                using var change = firewall.Apply(VpnAddress, names);
                File.Move(temporary, settingsPath, true);
                change.Commit();
                Volatile.Write(ref networks, next);
                return names.ToArray();
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }
    private static (uint Network, uint Mask) Parse(string subnet)
    {
        var parts = subnet.Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var prefix) || prefix is < 16 or > 32 ||
            !IPAddress.TryParse(parts[0], out var clientSubnet) ||
            clientSubnet.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || !IsPrivate(clientSubnet))
            throw new InvalidDataException("Los clientes deben pertenecer a subredes IPv4 privadas (prefijo /16 a /32).");
        var mask = prefix == 32 ? uint.MaxValue : uint.MaxValue << (32 - prefix);
        return (ToNumber(clientSubnet) & mask, mask);
    }
    public bool Allows(IPAddress? address)
    {
        if (address is null) return false;
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var value = ToNumber(address);
        return Volatile.Read(ref networks).Ranges.Any(s => (value & s.Mask) == s.Network);
    }
    private static bool IsPrivate(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168;
    }
    private static uint ToNumber(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4) throw new InvalidDataException("Subred IPv4 inválida.");
        return (uint)bytes[0] << 24 | (uint)bytes[1] << 16 | (uint)bytes[2] << 8 | bytes[3];
    }
}
