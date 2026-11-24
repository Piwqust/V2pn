namespace ServiceLib;

/// <summary>V2pn: what the server form checks before saving that v2rayN only finds when connecting.</summary>
public static class V2pnServerChecks
{
    /// <summary>
    /// The core that will run the server, as the form has it: the one chosen in the form, else the one Settings give
    /// the protocol (Xray unless changed).
    /// </summary>
    public static ECoreType EffectiveCore(EConfigType configType, string? formCoreType)
    {
        return Enum.TryParse<ECoreType>(formCoreType, out var chosen)
            ? chosen
            : AppManager.Instance.GetCoreType(null, configType);
    }

    /// <summary>
    /// Why the core cannot carry the server's transport (sing-box has no kcp, xhttp or XDRIVE, and only some transports
    /// for some protocols), or null when it can. The rules are v2rayN's own (NodeValidator, which refuses such a server
    /// at connect): a probe server is checked once with the transport and once with plain raw, and what only the
    /// transport adds is the answer.
    /// </summary>
    public static string? TransportError(EConfigType configType, string? network, ECoreType core)
    {
        if (network.IsNullOrEmpty() || network == Global.DefaultNetwork
            || configType.IsGroupType() || configType is EConfigType.Custom or EConfigType.Outbound)
        {
            return null;
        }
        var withTransport = NodeValidator.Validate(Probe(configType, network), core).Errors;
        var withRaw = NodeValidator.Validate(Probe(configType, Global.DefaultNetwork), core).Errors;
        return withTransport.Except(withRaw).FirstOrDefault();
    }

    private static ProfileItem Probe(EConfigType configType, string network)
    {
        return new ProfileItem
        {
            ConfigType = configType,
            Address = "example.com",
            Port = 443,
            Network = network,
        };
    }
}
