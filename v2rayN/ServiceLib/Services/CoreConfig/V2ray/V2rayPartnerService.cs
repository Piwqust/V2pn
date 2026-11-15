namespace ServiceLib.Services.CoreConfig;

// V2pn: a partner's reviewed transport.json and core.json, only for the servers of its subscription.
public partial class CoreConfigV2rayService
{
    /// <summary>Dials a partner's server through its core.json dialer and puts its transport on it.</summary>
    private void ApplyV2pnPartnerServer(Outbounds4Ray outbound)
    {
        if (context.V2pnTrafficFor(_node) is not { } traffic)
        {
            return;
        }
        if (traffic.CoreFor(ECoreType.Xray) is { } core)
        {
            context.V2pnPartnersUsed.Add(traffic.PartnerId);
            if (core.DialerFor(traffic.PartnerId) is { } dialer && outbound.streamSettings?.sockopt?.dialerProxy.IsNullOrEmpty() != false)
            {
                FillDialerProxy(outbound, dialer);
            }
        }
        if (_node.ConfigType != EConfigType.Outbound)
        {
            traffic.Transport?.ApplyXray(outbound);
        }
    }

    /// <summary>
    /// The rules of the partner whose server is connected, with its balancers and their observatories added to the
    /// config; they go after the pinned programs and before the person's routing.
    /// </summary>
    private List<RulesItem> BuildV2pnPartnerRules()
    {
        if (context.V2pnTrafficFor(_node) is not { } traffic || traffic.CoreFor(ECoreType.Xray) is not { } core
            || (core.Rules.Count == 0 && core.Balancers.Count == 0))
        {
            return [];
        }
        context.V2pnPartnersUsed.Add(traffic.PartnerId);
        core.MergeXrayRouting(_coreConfig, traffic.PartnerId);
        return core.RulesFor(traffic.PartnerId);
    }

    private bool IsV2pnPartnerTag(string? tag) =>
        context.V2pnTrafficFor(_node) is { } traffic && traffic.CoreFor(ECoreType.Xray)?.Owns(traffic.PartnerId, tag) == true;

    /// <summary>The generated config with the outbounds of every partner whose servers it reaches.</summary>
    private string ApplyCustomOutboundReplace()
    {
        var content = ApplyCustomOutboundReplaceRaw();
        if (context.V2pnPartnersUsed.Count == 0 || JsonUtils.ParseJson(content) is not JsonObject config)
        {
            return content;
        }
        foreach (var id in context.V2pnPartnersUsed)
        {
            if (context.V2pnPartnerSubs.Values.FirstOrDefault(t => t.PartnerId == id) is { } traffic)
            {
                traffic.CoreFor(ECoreType.Xray)?.MergeOutbounds(config, id, traffic.Transport);
            }
        }
        context.V2pnPartnersUsed.Clear();
        return JsonUtils.Serialize(config);
    }
}
