using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

// allowedSubnets lists every private network that may open the mobile view (VPN, VLANs);
// allowedSubnet is the original single-network format and is still accepted.
public sealed record VpnMobileSettings(string VpnAddress, string? AllowedSubnet = null, string[]? AllowedSubnets = null);
public sealed class VpnMobileNetwork
{
    public const int MaxSubnets = 20;
    private readonly (uint Network, uint Mask)[] subnets;
    public string VpnAddress { get; }
    public bool ListeningOnVpn { get; }
    public IReadOnlyList<string> AllowedSubnets { get; }
    public string Url => $"http://{VpnAddress}:5081/";
    public VpnMobileNetwork(string dataDir)
    {
        var settings = JsonSerializer.Deserialize<VpnMobileSettings>(File.ReadAllText(Path.Combine(dataDir, "mobile-vpn-settings.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Falta la configuración VPN móvil.");
        var address = IPAddress.Parse(settings.VpnAddress);
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || !IsPrivate(address))
            throw new InvalidDataException("El acceso móvil debe permanecer en una red IPv4 privada.");
        var configured = (settings.AllowedSubnets is { Length: > 0 } many ? many : settings.AllowedSubnet is { } one ? [one] : [])
            .Select(s => s.Trim()).Distinct().ToArray();
        if (configured.Length is 0 or > MaxSubnets) throw new InvalidDataException($"Configura entre 1 y {MaxSubnets} redes autorizadas para el acceso móvil.");
        subnets = configured.Select(Parse).ToArray();
        AllowedSubnets = Array.AsReadOnly(configured);
        VpnAddress = address.ToString();
        ListeningOnVpn = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Any(a => a.Address.Equals(address));
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
        return subnets.Any(s => (value & s.Mask) == s.Network);
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
