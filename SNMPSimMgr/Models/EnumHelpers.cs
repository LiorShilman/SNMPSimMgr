namespace SNMPSimMgr.Models;

public static class SnmpVersionOptionValues
{
    public static SnmpVersionOption[] All => Enum.GetValues<SnmpVersionOption>();
}

public static class AuthProtocolValues
{
    public static AuthProtocol[] All => Enum.GetValues<AuthProtocol>();
}

public static class PrivProtocolValues
{
    public static PrivProtocol[] All => Enum.GetValues<PrivProtocol>();
}

public static class SnmpTypeNames
{
    public static string[] All => new[]
    {
        "Integer32", "OctetString", "ObjectIdentifier", "IpAddress",
        "Counter32", "Gauge32", "TimeTicks", "Counter64"
    };
}
