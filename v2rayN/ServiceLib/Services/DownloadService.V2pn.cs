namespace ServiceLib.Services;

public partial class DownloadService
{
    /// <summary>
    /// V2pn: the panel headers (<see cref="SubPanelHeaders"/>) of the first successful response. A
    /// subscription fetches its main link before any extra links, so they describe the main one.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ResponseHeaders { get; private set; }

    /// <summary>V2pn: the status code of the last HTTP response, successful or not.</summary>
    public int? LastStatus { get; private set; }

    /// <summary>V2pn: the words of the last download error, for the subscription's update result.</summary>
    public string? LastError { get; set; }

    private HttpMessageHandler KeepResponseHeaders(HttpMessageHandler inner) => new ResponseHeadersHandler(inner, this);

    private sealed class ResponseHeadersHandler(HttpMessageHandler inner, DownloadService owner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            owner.LastStatus = (int)response.StatusCode;
            if (owner.ResponseHeaders == null && response.IsSuccessStatusCode)
            {
                owner.ResponseHeaders = SubPanelHeaders.Collect(response);
            }
            return response;
        }
    }
}
