using System.Net;
using System.Text.Json;

static class MobileNetworkSettingsTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var dir = Path.Combine(root, "network-settings"); Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "mobile-vpn-settings.json");
        File.WriteAllText(path, "{\"vpnAddress\":\"10.250.0.1\",\"allowedSubnet\":\"10.20.30.0/24\",\"extraField\":\"preserved\"}");
        var network = new VpnMobileNetwork(dir);
        var firewall = new FakeFirewall();
        var saved = network.UpdateAllowedSubnets(["192.168.50.0/24", "10.20.30.7/32"], firewall);
        var json = JsonDocument.Parse(File.ReadAllText(path));
        check(firewall.Current.SequenceEqual(saved) && firewall.Commits == 1 && network.Allows(IPAddress.Parse("192.168.50.8")) &&
            network.Allows(IPAddress.Parse("10.20.30.7")) && !network.Allows(IPAddress.Parse("10.20.30.8")),
            "Rangos: aplica redes/IP individuales a firewall y acceso activo sin reinicio");
        check(json.RootElement.GetProperty("extraField").GetString() == "preserved" && !json.RootElement.TryGetProperty("allowedSubnet", out _) && network.VpnAddress == "10.250.0.1",
            "Rangos: conserva IP del servidor y campos adicionales, migra lista anterior");
        saved[0] = "tampered";
        check(network.AllowedSubnets[0] == "192.168.50.0/24", "Rangos: la respuesta no permite mutar la política activa");
        var reloaded = new VpnMobileNetwork(dir);
        check(reloaded.AllowedSubnets.SequenceEqual(network.AllowedSubnets) && reloaded.Allows(IPAddress.Parse("192.168.50.8")),
            "Rangos: configuración aplicada se conserva al reiniciar");
        var before = File.ReadAllBytes(path); var calls = firewall.Calls;
        foreach (var input in new string[]?[] { null, [], [""], ["8.8.8.0/24"], ["10.0.0.0/8"], ["10.0.0.0/33"], ["invalid"], ["::1/32"], Enumerable.Repeat("10.20.30.0/24", 21).ToArray(), ["192.168.50.0/24", "8.8.8.8/32"] })
        {
            var rejected = false;
            try { network.UpdateAllowedSubnets(input, firewall); } catch (ArgumentException) { rejected = true; }
            check(rejected && firewall.Calls == calls && before.SequenceEqual(File.ReadAllBytes(path)), "Rangos: lista inválida no modifica firewall ni datos");
        }
        firewall.Deny = true;
        var denied = false;
        try { network.UpdateAllowedSubnets(["10.70.0.0/24"], firewall); } catch (UnauthorizedAccessException) { denied = true; }
        check(denied && before.SequenceEqual(File.ReadAllBytes(path)) && network.Allows(IPAddress.Parse("192.168.50.8")) && !File.Exists(path + ".tmp"),
            "Rangos: firewall sin permisos conserva configuración y acceso anteriores");
        firewall.Deny = false;
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = false;
            try { network.UpdateAllowedSubnets(["10.70.0.0/24"], firewall); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; }
            check(failed && firewall.Rollbacks == 1 && firewall.Current.SequenceEqual(network.AllowedSubnets) && before.SequenceEqual(File.ReadAllBytes(path)),
                "Rangos: fallo de guardado revierte firewall y conserva archivo/política activa");
        }
        network.UpdateAllowedSubnets([" 10.70.0.0/24 ", "10.70.0.0/24"], firewall);
        check(network.AllowedSubnets.Count == 1 && network.Allows(IPAddress.Parse("10.70.0.8")) && !network.Allows(IPAddress.Parse("192.168.50.8")),
            "Rangos: recorta/evita duplicados y revoca el acceso de redes retiradas");
        check(network.Allows(IPAddress.Loopback) && network.Allows(IPAddress.Parse("10.70.0.8").MapToIPv6()),
            "Rangos: conserva acceso local y compatibilidad IPv4 sobre IPv6");
    }
    sealed class FakeFirewall : IMobileFirewall
    {
        public string[] Current { get; private set; } = ["10.20.30.0/24"];
        public int Calls, Commits, Rollbacks;
        public bool Deny;
        public IMobileFirewallChange Apply(string address, string[] ranges)
        {
            Calls++; if (Deny) throw new UnauthorizedAccessException();
            var old = Current; Current = ranges.ToArray(); return new Change(this, old);
        }
        sealed class Change(FakeFirewall owner, string[] previous) : IMobileFirewallChange
        {
            private bool committed;
            public void Commit() { committed = true; owner.Commits++; }
            public void Dispose() { if (!committed) { owner.Current = previous; owner.Rollbacks++; } }
        }
    }
}
