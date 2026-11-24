namespace ServiceLib.Handler;

public class SubscriptionHandler(
    Config config,
    bool blProxy,
    Func<bool, string, Task> updateFunc)
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _updateGates = new();

    /// <summary>V2pn: <see cref="SubInfoItem.LastResultDetail"/> of a download that got no answer in time.</summary>
    public const string SubTimeoutDetail = "timeout";

    private readonly Config _config = config;
    private readonly bool _blProxy = blProxy;

    public async Task UpdateProcess(string subId)
    {
        await updateFunc?.Invoke(false, ResUI.MsgUpdateSubscriptionStart);
        var subItem = await ConfigDbQuery.SubItems();

        if (subItem is not { Count: > 0 })
        {
            await updateFunc?.Invoke(false, ResUI.MsgNoValidSubscription);
            return;
        }

        await ConfigTextHandler.ResetDefaultServer();
        var successCount = 0;
        foreach (var listed in subItem)
        {
            if (!IsValidSubscription(listed, subId))
            {
                continue;
            }
            // V2pn: one update of a subscription at a time. A new one is fetched on adding and, within the minute,
            // by the scheduler too (it has never been updated); two imports of one subscription at once could
            // interleave their server lists.
            var gate = _updateGates.GetOrAdd(listed.Id, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                // The list was read before waiting; the other update may have changed this subscription since.
                var item = await ConfigDbQuery.GetSubItem(listed.Id) ?? listed;
                var hashCode = $"{item.Remarks}->";
                if (!item.Enabled)
                {
                    await updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgSkipSubscriptionUpdate}");
                    continue;
                }

                using var processor = new SubscriptionHandlerProcessor(this, item, hashCode);
                await updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgStartGettingSubscriptions}");

                // Get all subscription content (main subscription + additional subscriptions)
                var result = await processor.DownloadAllSubscriptions();

                // Process download result
                var imported = await ProcessDownloadResult(item.Id, result, hashCode);
                var downloadHandle = processor.DownloadService;
                // V2pn: a verified partner's panel may move the subscription to another of its domains.
                if (imported)
                {
                    await MoveIfAskedAsync(item, downloadHandle.ResponseHeaders, hashCode);
                }
                // V2pn: name, traffic, expiry and the Happ/INCY values from the panel's headers and the body's "#key: value" lines.
                await SubInfoManager.Instance.ApplyAsync(_config, item, downloadHandle.ResponseHeaders, imported, result);
                var (outcome, detail) = Classify(imported, result, downloadHandle.ResponseHeaders != null,
                    downloadHandle.LastStatus, downloadHandle.LastError);
                await SubInfoManager.Instance.RecordAttemptAsync(item.Id, outcome, detail);
                if (imported)
                {
                    successCount++;
                    // Upstream records the time only for scheduled updates; the UI shows it for every update.
                    // Always forward: the UI tells a failed update by an unchanged time, and two updates can land
                    // in the same second.
                    item.UpdateTime = Math.Max(((DateTimeOffset)DateTime.Now).ToUnixTimeSeconds(), item.UpdateTime + 1);
                    await ConfigDbHandler.AddSubItem(item);
                }

                await updateFunc?.Invoke(false, "-------------------------------------------------------");
            }
            catch (Exception ex)
            {
                var hashCode = $"{listed.Remarks}->";
                Logging.SaveLog("UpdateSubscription", ex);
                await SubInfoManager.Instance.RecordAttemptAsync(listed.Id, EV2pnSubUpdateResult.Failed, ex.Message);
                await updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgFailedImportSubscription}: {ex.Message}");
                await updateFunc?.Invoke(false, "-------------------------------------------------------");
            }
            finally
            {
                gate.Release();
            }
        }

        await updateFunc?.Invoke(successCount > 0, $"{ResUI.MsgUpdateSubscriptionEnd}");
    }

    /// <summary>
    /// V2pn: new-url or new-domain from the panel (HTTP headers only), honoured for a subscription whose verified
    /// partner's listing names the new host (V2pnSubDefaults.MovedUrl). The subscription keeps its id, name and
    /// settings; only its address changes, and the log says so.
    /// </summary>
    private async Task MoveIfAskedAsync(SubItem item, IReadOnlyDictionary<string, string>? headers, string hashCode)
    {
        string? Get(string name) => headers?.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        var newUrl = Get(SubPanelHeaders.NewUrl);
        var newDomain = Get(SubPanelHeaders.NewDomain);
        if (newUrl.IsNullOrEmpty() && newDomain.IsNullOrEmpty())
        {
            return;
        }
        var partner = V2pnPartners.Instance.ForSubscription(item);
        var target = V2pnSubDefaults.MovedUrl(item.Url, newUrl, newDomain, partner?.Listing);
        if (target == null)
        {
            Logging.SaveLog($"V2pn: {Utils.TryUri(item.Url)?.Host} asked to move the subscription; ignored (the new address is not a domain of a verified partner)");
            return;
        }
        var subs = await ConfigDbQuery.SubItems() ?? [];
        if (subs.Any(s => s.Id != item.Id && string.Equals(s.Url?.Trim(), target, StringComparison.Ordinal)))
        {
            Logging.SaveLog("V2pn: the subscription's new address belongs to another subscription already; not moved");
            return;
        }
        var from = Utils.TryUri(item.Url)?.Host;
        item.Url = target;
        await ConfigDbHandler.AddSubItem(item);
        var to = Utils.TryUri(target)?.Host ?? target;
        Logging.SaveLog($"V2pn: subscription {item.Id} moved from {from} to {to} ({partner!.Id})");
        await updateFunc?.Invoke(false, $"{hashCode}{string.Format(ResUI.MsgV2pnSubMoved, to)}");
    }

    /// <summary>
    /// V2pn: tells an unreachable site from one that answered with nothing or with something that is not a list of
    /// servers. <paramref name="answered"/>: a successful HTTP response came back.
    /// </summary>
    internal static (EV2pnSubUpdateResult Result, string? Detail) Classify(bool imported, string? body, bool answered,
        int? lastStatus, string? lastError)
    {
        if (imported)
        {
            return (EV2pnSubUpdateResult.Ok, null);
        }
        if (body.IsNotEmpty())
        {
            return (EV2pnSubUpdateResult.Invalid, null);
        }
        if (answered)
        {
            return (EV2pnSubUpdateResult.Empty, null);
        }
        if (lastStatus is { } status and not (>= 200 and < 300))
        {
            return (EV2pnSubUpdateResult.Unreachable, $"HTTP {status}");
        }
        return (EV2pnSubUpdateResult.Unreachable, lastError);
    }

    private bool IsValidSubscription(SubItem item, string subId)
    {
        var id = item.Id.TrimEx();
        var url = item.Url.TrimEx();

        if (id.IsNullOrEmpty() || url.IsNullOrEmpty())
        {
            return false;
        }

        if (subId.IsNotEmpty() && item.Id != subId)
        {
            return false;
        }

        if (!url.StartsWith(Global.HttpsProtocol) && !url.StartsWith(Global.HttpProtocol))
        {
            return false;
        }

        return true;
    }

    private async Task<bool> ProcessDownloadResult(string id, string result, string hashCode)
    {
        if (result.IsNullOrEmpty())
        {
            await updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgSubscriptionDecodingFailed}");
            return false;
        }

        await updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgGetSubscriptionSuccessfully}");

        // If result is too short, display content directly
        if (result.Length < 99)
        {
            await updateFunc?.Invoke(false, $"{hashCode}{result}");
        }

        await updateFunc?.Invoke(false, $"{hashCode}{ResUI.MsgStartParsingSubscription}");

        // Add servers to configuration
        var ret = await ConfigDbHandler.AddBatchServers(result, id, true);
        if (ret <= 0)
        {
            Logging.SaveLog($"FailedImportSubscription: {V2pnLimits.Body(result)}");
        }

        // Update completion message
        await updateFunc?.Invoke(false, ret > 0
            ? $"{hashCode}{ResUI.MsgUpdateSubscriptionEnd}"
            : $"{hashCode}{ResUI.MsgFailedImportSubscription}");

        return ret > 0;
    }

    private class SubscriptionHandlerProcessor : IDisposable
    {
        private readonly DownloadService[] _downloadServices;
        private readonly SubscriptionHandler _handler;
        private readonly SubItem _item;
        private readonly string _hashCode;

        public SubscriptionHandlerProcessor(SubscriptionHandler handler, SubItem item, string hashCode)
        {
            _handler = handler;
            _item = item;
            _hashCode = hashCode;
            _downloadServices = CreateDownloadHandler();
        }

        /// <summary>V2pn: the service whose response headers, status and error describe the main link.</summary>
        public DownloadService DownloadService => _downloadServices[0];

        private DownloadService[] CreateDownloadHandler()
        {
            if (!HttpRequestHeadersHelper.TryParse(_item.RequestHeaders, out var requestHeaders))
            {
                throw new FormatException(ResUI.SubRequestHeadersInvalid);
            }

            var userAgent = V2pnSubs.UserAgentFor(_item);
            if (userAgent.IsNullOrEmpty())
            {
                userAgent = Utils.GetVersion(false);
            }
            // V2pn: the direct route falls back to the VPN only while the core runs. A provider's site blocked or
            // throttled on the direct route failed a manual update (and every new subscription) now and then, while
            // the running VPN could reach it; with the core stopped a second try would only hide the real error.
            var handler = _handler._blProxy || CoreManager.Instance.IsCoreAlive
                ? HttpClientHelper.Instance.CreateFallbackHandler(_handler._blProxy, new())
                : HttpClientHelper.CreateSocketsHttpHandler(false, new());
            var headersHandler = HttpRequestHeadersHelper.CreateHandler(handler, userAgent, "*/*", requestHeaders);
            // V2pn: keeps the response headers, where panels put a subscription's name, traffic and expiry.
            var downloadHandle = new DownloadService
            {
                Handler = DownloadService.KeepResponseHeaders(headersHandler),
                DisposeHandler = true,
            };
            downloadHandle.Error += (sender2, args) =>
            {
                var error = args.GetException();
                // A timeout's own words ("The operation was canceled.") say nothing to a person; the UI words it.
                downloadHandle.LastError = error is OperationCanceledException or TimeoutException
                    ? SubTimeoutDetail
                    : error.Message;
                _handler.ReportError($"{_hashCode}{error.Message}");
            };
            return [downloadHandle];
        }

        private async Task<string> DownloadSubscriptionContent(string url)
        {
            foreach (var downloadService in _downloadServices)
            {
                var result = await downloadService.TryDownloadString(url);
                if (!result.IsNullOrEmpty())
                {
                    return result;
                }
            }

            return string.Empty;
        }

        public async Task<string> DownloadAllSubscriptions()
        {
            // Download main subscription content
            var result = await DownloadMainSubscription();

            // Process additional subscription links (if any)
            if (_item.ConvertTarget.IsNullOrEmpty() && _item.MoreUrl.TrimEx().IsNotEmpty())
            {
                result = await DownloadAdditionalSubscriptions(result);
            }

            return result;
        }

        private async Task<string> DownloadMainSubscription()
        {
            // Prepare subscription URL and download directly
            var url = Utils.GetPunycode(_item.Url.TrimEx());

            // If conversion is needed
            if (_item.ConvertTarget.IsNotEmpty())
            {
                var subConvertUrl = _handler._config.ConstItem.SubConvertUrl.IsNullOrEmpty()
                    ? Global.SubConvertUrls.FirstOrDefault()
                    : _handler._config.ConstItem.SubConvertUrl;

                url = string.Format(subConvertUrl!, Utils.UrlEncode(url));

                if (!url.Contains("target="))
                {
                    url += $"&target={_item.ConvertTarget}";
                }

                if (!url.Contains("config="))
                {
                    url += $"&config={Global.SubConvertConfig.FirstOrDefault()}";
                }
            }

            // Download and return result directly
            return await DownloadSubscriptionContent(url);
        }

        private async Task<string> DownloadAdditionalSubscriptions(string mainResult)
        {
            var result = mainResult;

            // If main subscription result is Base64 encoded, decode it first
            if (result.IsNotEmpty() && Utils.IsBase64String(result))
            {
                result = Utils.Base64Decode(result);
            }

            // Process additional URL list
            var lstUrl = _item.MoreUrl.TrimEx().Split(",") ?? [];
            foreach (var it in lstUrl)
            {
                var url2 = Utils.GetPunycode(it);
                if (url2.IsNullOrEmpty())
                {
                    continue;
                }

                var additionalResult = await DownloadSubscriptionContent(url2);

                if (additionalResult.IsNotEmpty())
                {
                    // Process additional subscription results, add to main result
                    if (Utils.IsBase64String(additionalResult))
                    {
                        result += Environment.NewLine + Utils.Base64Decode(additionalResult);
                    }
                    else
                    {
                        result += Environment.NewLine + additionalResult;
                    }
                }
            }

            return result;
        }

        public void Dispose()
        {
            foreach (var downloadService in _downloadServices)
            {
                downloadService.Dispose();
            }
        }
    }

    private void ReportError(string message)
    {
        updateFunc?.Invoke(false, message);
    }
}
