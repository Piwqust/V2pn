namespace ServiceLib.Services.CoreConfig;

// V2pn: what "Обход блокировок" adds to an Xray config (V2pnBypassPlan).
public partial class CoreConfigV2rayService
{
    internal const string CalmFingerprint = "chrome";

    /// <summary>
    /// The way found for this server, and the calm defaults: a browser's TLS fingerprint where the server's link left
    /// it empty (Go's own one gives a proxy away), and XHTTP connection reuse written out instead of left to the core,
    /// whose defaults change between versions and could open a burst of connections at start. A partner's reviewed
    /// transport for its own servers stays as it is.
    /// </summary>
    private void ApplyV2pnBypass(Outbounds4Ray outbound)
    {
        var plan = context.V2pnBypass;
        if (plan.IsOff || _node.ConfigType == EConfigType.Outbound || outbound.streamSettings is not { } stream)
        {
            return;
        }
        if (plan.For(_node) is { } variant && context.V2pnTrafficFor(_node)?.Transport == null)
        {
            variant.Transport.ApplyXray(outbound);
        }
        if (!plan.Calm)
        {
            return;
        }
        if (stream.tlsSettings is { } tls && tls.fingerprint.IsNullOrEmpty())
        {
            tls.fingerprint = CalmFingerprint;
        }
        if (stream.realitySettings is { } reality && reality.fingerprint.IsNullOrEmpty())
        {
            reality.fingerprint = CalmFingerprint;
        }
        if (stream.xhttpSettings is { } xhttp && xhttp.extra?["xmux"] == null)
        {
            var extra = xhttp.extra ?? new JsonObject();
            extra["xmux"] = CalmXmux();
            xhttp.extra = extra;
        }
    }

    /// <summary>Xray's documented XHTTP defaults, written out; ranges, so no two connections live by the same clock.</summary>
    internal static JsonObject CalmXmux() => new()
    {
        ["maxConcurrency"] = "16-32",
        ["maxConnections"] = 0,
        ["cMaxReuseTimes"] = 0,
        ["hMaxRequestTimes"] = "600-900",
        ["hMaxReusableSecs"] = "1800-3000",
    };

    /// <summary>Sites that go direct get their TLS handshake split, as the server connections do (V2pnBypassItem.DirectFragment).</summary>
    private void ApplyV2pnDirectFragment()
    {
        if (!context.V2pnBypass.DirectFragment)
        {
            return;
        }
        foreach (var outbound in _coreConfig.outbounds.Where(o => o.tag == Global.DirectTag && o.protocol == "freedom"))
        {
            outbound.streamSettings ??= new();
            var finalmask = outbound.streamSettings.finalmask ?? new JsonObject();
            if (finalmask["tcp"] is JsonArray { Count: > 0 })
            {
                continue;
            }
            finalmask["tcp"] = new JsonArray(V2pnBypass.Fragment.Transport.XrayFragmentMask(false));
            outbound.streamSettings.finalmask = finalmask;
        }
    }
}
