using System.Runtime.InteropServices;

public interface IMobileFirewall
{
    IMobileFirewallChange Apply(string serverAddress, string[] subnets);
}
public interface IMobileFirewallChange : IDisposable
{
    void Commit();
}

// Changes only the existing Vision application rule; never creates a broad port rule or elevates the service.
public sealed class MobileFirewall(string applicationPath) : IMobileFirewall
{
    public IMobileFirewallChange Apply(string serverAddress, string[] subnets)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        object? policy = null, rules = null, rule = null;
        try
        {
            policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")
                ?? throw new InvalidOperationException("Windows Firewall no está disponible."));
            if ((int)((dynamic)policy!).LocalPolicyModifyState != 0)
                throw new InvalidOperationException("Una política de Windows impide aplicar cambios locales al firewall.");
            rules = ((dynamic)policy!).Rules;
            rule = ((dynamic)rules).Item("VisionServidorVPN");
            dynamic configured = rule;
            var localAddresses = ((string)configured.LocalAddresses).Split(',').Select(s => s.Trim().Split('/')[0]).ToArray();
            if ((int)configured.Protocol != 6 || (int)configured.Direction != 1 || (int)configured.Action != 1 ||
                !(bool)configured.Enabled || (string)configured.LocalPorts != "5081" ||
                localAddresses.Length != 1 || localAddresses[0] != serverAddress ||
                !string.Equals(Path.GetFullPath((string)configured.ApplicationName), Path.GetFullPath(applicationPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("La regla VisionServidorVPN no corresponde al acceso móvil de Vision. Revisa su configuración en Server.");
            // Preserve all other rule properties, including local address, profiles and port.
            var previous = (string)configured.RemoteAddresses;
            configured.RemoteAddresses = string.Join(",", subnets);
            return new Change(policy!, rules, rule, previous);
        }
        catch
        {
            Release(rule); Release(rules); Release(policy);
            throw;
        }
    }
    private static void Release(object? value)
    {
        if (OperatingSystem.IsWindows() && value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
    private sealed class Change(object policy, object rules, object rule, string previous) : IMobileFirewallChange
    {
        private bool committed;
        public void Commit() => committed = true;
        public void Dispose()
        {
            try { if (!committed) ((dynamic)rule).RemoteAddresses = previous; }
            finally { Release(rule); Release(rules); Release(policy); }
        }
    }
}
