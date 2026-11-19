namespace ServiceLib.Services.CoreConfig;

// V2pn: a partner's reviewed transport.json and core.json, only for the servers of its subscription.
public partial class CoreConfigSingboxService
{
    /// <summary>
    /// The partner's transport turns sing-box's TLS fragment on for its servers, whatever the person's own setting;
    /// not for a server dialed through the partner's relay (core.json dialer), as with Xray.
    /// </summary>
    private bool V2pnPartnerFragment =>
        context.V2pnTrafficFor(_node) is { Transport.Fragment: not null } traffic && traffic.CoreFor(ECoreType.sing_box)?.Dialer == null;

    /// <summary>The way "Обход блокировок" found for this server; a partner's reviewed transport goes first.</summary>
    private bool V2pnBypassFragment =>
        context.V2pnBypass.For(_node) != null && context.V2pnTrafficFor(_node)?.Transport == null;

    /// <summary>Dials a partner's server through its core.json dialer.</summary>
    private void ApplyV2pnPartnerServer(BaseServer4Sbox outbound)
    {
        if (context.V2pnTrafficFor(_node) is not { } traffic || traffic.CoreFor(ECoreType.sing_box) is not { } core)
        {
            return;
        }
        context.V2pnPartnersUsed.Add(traffic.PartnerId);
        if (core.DialerFor(traffic.PartnerId) is { } dialer && outbound.detour.IsNullOrEmpty())
        {
            outbound.detour = dialer;
        }
    }

    /// <summary>The rules of the partner whose server is connected; they go after the person's own rules and before the routing's catch-all default.</summary>
    private List<RulesItem> BuildV2pnPartnerRules()
    {
        if (context.V2pnTrafficFor(_node) is not { } traffic || traffic.CoreFor(ECoreType.sing_box) is not { } core || core.Rules.Count == 0)
        {
            return [];
        }
        context.V2pnPartnersUsed.Add(traffic.PartnerId);
        return core.RulesFor(traffic.PartnerId);
    }

    private bool IsV2pnPartnerTag(string? tag) =>
        context.V2pnTrafficFor(_node) is { } traffic && traffic.CoreFor(ECoreType.sing_box)?.Owns(traffic.PartnerId, tag) == true;

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
                traffic.CoreFor(ECoreType.sing_box)?.MergeOutbounds(config, id, traffic.Transport);
            }
        }
        context.V2pnPartnersUsed.Clear();
        return JsonUtils.Serialize(config);
    }
}
