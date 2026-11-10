namespace Kosync.Services;

// The one spelling of a sync log line's prefix: when it happened, and which
// client it came from. Shared by SyncController and SyncTraceMiddleware, so
// a trace entry interleaves with the endpoint's own lines instead of reading
// like it came from a different server.
//
// Deep enough that its callers stop knowing how the client IP is arrived at:
// whether to believe an X-Forwarded-For, and how to say "this did not come
// through your proxy", are decisions that live here and behind IPService
// rather than at every log site.
public class RequestLogFormatter
{
    private readonly IPService _ipService;
    private readonly ProxyService _proxyService;

    public RequestLogFormatter(IPService ipService, ProxyService proxyService)
    {
        _ipService = ipService;
        _proxyService = proxyService;
    }

    public string Prefixed(string text)
    {
        string prefix = $"[{DateTime.Now.ToString(Constants.Logging.TimestampFormat)}] [{_ipService.ClientIP}]";

        // If trusted proxies are set but this request comes from another
        // address, mark it.
        if (_proxyService.TrustedProxies.Length > 0 &&
            !_ipService.TrustedProxy)
        {
            prefix += Constants.Logging.UntrustedSourceMarker;
        }

        return $"{prefix} {text}";
    }
}
