namespace ServiceLib.Services;

public partial class DownloadService
{
    /// <summary>
    /// V2pn: the panel headers (<see cref="SubPanelHeaders"/>) of the first successful response. A
    /// subscription fetches its main link before any extra links, so they describe the main one.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ResponseHeaders { get; private set; }

    private HttpMessageHandler KeepResponseHeaders(HttpMessageHandler inner) => new ResponseHeadersHandler(inner, this);

    private sealed class ResponseHeadersHandler(HttpMessageHandler inner, DownloadService owner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (owner.ResponseHeaders == null && response.IsSuccessStatusCode)
            {
                owner.ResponseHeaders = SubPanelHeaders.Collect(response);
            }
            return response;
        }
    }
}
