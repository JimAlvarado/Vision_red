using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

public sealed record VpnMobileSettings(string VpnAddress, string AllowedSubnet);
public sealed class VpnMobileNetwork
{
    private readonly uint network;
    private readonly uint mask;
    public string VpnAddress { get; }
    public bool ListeningOnVpn { get; }
    public string Url => $"http://{VpnAddress}:5081/";
    public VpnMobileNetwork(string dataDir)
    {
        var settings = JsonSerializer.Deserialize<VpnMobileSettings>(File.ReadAllText(Path.Combine(dataDir, "mobile-vpn-settings.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException("Falta la configuración VPN móvil.");
        var address = IPAddress.Parse(settings.VpnAddress);
        var parts = settings.AllowedSubnet.Split('/');
        var prefix = int.Parse(parts[1]);
        if (prefix is < 16 or > 32 || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !IsPrivate(address)) throw new InvalidDataException("El acceso móvil debe permanecer en una red IPv4 privada.");
        mask = prefix == 32 ? uint.MaxValue : uint.MaxValue << (32 - prefix);
        var clientSubnet=IPAddress.Parse(parts[0]);
        if(clientSubnet.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork || !IsPrivate(clientSubnet))
            throw new InvalidDataException("Los clientes deben pertenecer a una subred IPv4 privada.");
        network = ToNumber(clientSubnet) & mask;
        VpnAddress = address.ToString();
        ListeningOnVpn = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses).Any(a => a.Address.Equals(address));
    }
    public bool Allows(IPAddress? address)
    {
        if (address is null) return false;
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && (ToNumber(address) & mask) == network;
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
