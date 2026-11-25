namespace ServiceLib.Services.CoreConfig;

// V2pn: servers taken out of a provider's whole Xray config (V2pnProviderConfigs) run with the provider's outbound.
public partial class CoreConfigV2rayService
{
    /// <summary>
    /// Puts the provider's own outbound in place of <paramref name="outbound"/>, the one V2pn built from the server's
    /// fields: the provider's keeps what a link cannot hold (ws headers, xhttp extras, finalmask). The tag, the
    /// dialer and the interface V2pn sets on <paramref name="outbound"/> carry over (ApplyCustomOutboundReplaceRaw).
    /// When "Обход блокировок" found its own way for this server, or a partner's reviewed transport applies, V2pn's
    /// outbound stays: both change the transport, and the provider's would undo it.
    /// </summary>
    private void UseV2pnProviderOutbound(Outbounds4Ray outbound)
    {
        if (_node.ConfigType == EConfigType.Outbound
            || _node.V2pnOutbound.IsNullOrEmpty()
            || !context.CustomOutboundContent.TryGetValue(_node.IndexId, out var content)
            || context.V2pnBypass.For(_node) != null
            || context.V2pnTrafficFor(_node)?.Transport != null)
        {
            return;
        }
        if (context.V2pnBypass.Calm)
        {
            context.CustomOutboundContent[_node.IndexId] = CalmProviderOutbound(content);
        }
        context.CustomOutboundMap[outbound] = _node.IndexId;
    }

    /// <summary>The calm defaults of the bypass (ApplyV2pnBypass) on the provider's outbound, where it left them out.</summary>
    internal static string CalmProviderOutbound(string content)
    {
        if (JsonUtils.ParseJson(content) is not JsonObject outbound || outbound["streamSettings"] is not JsonObject stream)
        {
            return content;
        }
        foreach (var key in new[] { "tlsSettings", "realitySettings" })
        {
            if (stream[key] is JsonObject security && security["fingerprint"]?.ToString().IsNullOrEmpty() != false)
            {
                security["fingerprint"] = CalmFingerprint;
            }
        }
        if (stream["xhttpSettings"] is JsonObject xhttp)
        {
            var extra = xhttp["extra"] as JsonObject ?? new JsonObject();
            if (extra["xmux"] == null)
            {
                extra["xmux"] = CalmXmux();
                xhttp["extra"] = extra;
            }
        }
        return outbound.ToJsonString();
    }
}
