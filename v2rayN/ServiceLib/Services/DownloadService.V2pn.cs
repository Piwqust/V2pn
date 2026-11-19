namespace ServiceLib.Services;

public partial class DownloadService
{
    /// <summary>
    /// V2pn: the panel headers (<see cref="SubPanelHeaders"/>) of the first successful response, when
    /// <see cref="Handler"/> was made by <see cref="KeepResponseHeaders"/>. A subscription fetches its main link
    /// before any extra links, so they describe the main one.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ResponseHeaders => (Handler as ResponseHeadersHandler)?.Headers;

    /// <summary>V2pn: the status code of the last HTTP response, successful or not (with <see cref="KeepResponseHeaders"/>).</summary>
    public int? LastStatus => (Handler as ResponseHeadersHandler)?.LastStatus;

    /// <summary>V2pn: the words of the last download error, for the subscription's update result.</summary>
    public string? LastError { get; set; }

    /// <summary>V2pn: wraps a handler so the service built on it keeps the response headers and status.</summary>
    public static HttpMessageHandler KeepResponseHeaders(HttpMessageHandler inner) => new ResponseHeadersHandler(inner);

    private sealed class ResponseHeadersHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public IReadOnlyDictionary<string, string>? Headers { get; private set; }

        public int? LastStatus { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            LastStatus = (int)response.StatusCode;
            if (Headers == null && response.IsSuccessStatusCode)
            {
                Headers = SubPanelHeaders.Collect(response);
            }
            return response;
        }
    }
}
