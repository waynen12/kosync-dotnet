using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Kosync.Middleware;

// Writes down what a device actually sent and what it actually got back, for
// the cases the endpoints' own logging cannot see (Constants.SyncTrace).
//
// Sits outside the pipeline rather than inside a controller action
// deliberately: a request to a path we do not serve, with a verb we do not
// accept, or carrying a body model binding refuses never reaches an action
// at all. Those answer 404, 405 and 400 with nothing in the log, which is a
// reader whose sync silently stops and a server that claims it was never
// asked.
//
// Off unless an operator turned it on, and when off this costs one boolean
// per request.
public class SyncTraceMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SyncTraceOptions _options;

    // Matches a sensitive field and its value in a body that is *not* JSON -
    // a form-encoded post, or JSON too malformed to parse. Both spellings,
    // because `password=x` and `"password":"x"` are the two ways one arrives.
    // The JSON path below is what handles well-formed bodies.
    private static readonly Regex[] _redactedBodyPatterns =
        Constants.SyncTrace.RedactedBodyFields
            .SelectMany(field => new[]
            {
                $"(\"{Regex.Escape(field)}\"\\s*:\\s*)\"(?:[^\"\\\\]|\\\\.)*\"",
                $"(\\b{Regex.Escape(field)}=)[^&\\s]*",
            })
            .Select(pattern => new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToArray();

    public SyncTraceMiddleware(RequestDelegate next, IOptions<SyncTraceOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(
        HttpContext context,
        RequestLogFormatter formatter,
        ILogger<SyncTraceMiddleware> logger)
    {
        if (!_options.IsEnabled || !IsClientRequest(context.Request))
        {
            await _next(context);
            return;
        }

        // Before the body is read, because reading it consumes the stream
        // once and the endpoint still has to be able to bind it. Without
        // this, turning the trace on would break every push.
        context.Request.EnableBuffering();

        string requestBody = await ReadRequestBodyAsync(context.Request);

        logger.LogInformation(formatter.Prefixed(
            string.Format(
                Constants.SyncTrace.RequestFormat,
                context.TraceIdentifier,
                context.Request.Method,
                RequestTarget(context.Request))
            + Section(Headers(context.Request.Headers))
            + Section(BodyText(requestBody))));

        // The response is written to a buffer so it can be read and then
        // handed on. Safe here only because this runs for the protocol
        // surface, whose answers are a line of JSON - it is not a thing to
        // do to the portal's pages.
        var clientStream = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        var elapsed = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            elapsed.Stop();
            context.Response.Body = clientStream;

            // Whatever was half-written into the buffer is dropped rather
            // than flushed. Handing a partial body to the client would start
            // the response, and the error handler outside this middleware
            // could then no longer answer at all - so a traced request would
            // fail differently from an untraced one, which is the one thing a
            // diagnostic must never do.
            logger.LogError(exception, formatter.Prefixed(string.Format(
                Constants.SyncTrace.FaultFormat,
                context.TraceIdentifier,
                elapsed.ElapsedMilliseconds)));

            throw;
        }

        elapsed.Stop();
        context.Response.Body = clientStream;

        buffer.Position = 0;
        string responseBody = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();

        buffer.Position = 0;
        await buffer.CopyToAsync(clientStream);

        logger.LogInformation(formatter.Prefixed(
            string.Format(
                Constants.SyncTrace.ResponseFormat,
                context.TraceIdentifier,
                context.Response.StatusCode,
                elapsed.ElapsedMilliseconds)
            + Section(Headers(context.Response.Headers))
            + Section(BodyText(responseBody))));
    }

    private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);

        string body = await reader.ReadToEndAsync();

        // Back to the start, so the endpoint reads the body this just read.
        request.Body.Position = 0;

        return body;
    }

    // Every header, not a chosen few: the point of the trace is the exchange
    // we did not anticipate, so picking which headers matter in advance is
    // the one thing it cannot afford to do. On the way out that includes
    // Content-Type, which is the next question to ask of a client that
    // rejects an answer it was given.
    private static string Headers(IHeaderDictionary headers) =>
        string.Join(
            Constants.SyncTrace.HeaderSeparator,
            headers.Select(header => string.Format(
                Constants.SyncTrace.HeaderFormat,
                header.Key,
                IsRedactedHeader(header.Key)
                    ? Constants.SyncTrace.RedactedValue
                    : header.Value.ToString())));

    private static bool IsRedactedHeader(string name) =>
        Constants.SyncTrace.RedactedHeaders.Contains(name, StringComparer.OrdinalIgnoreCase);

    // Says so out loud when there was no body, because a line that just
    // stops reads like the trace lost something.
    private string BodyText(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) { return Constants.SyncTrace.EmptyBodyText; }

        return Truncated(Redacted(body));
    }

    // A body with nothing sensitive in it goes to the log verbatim, which
    // matters: "what exactly did the device send" is the question, and a
    // re-serialised body is no longer an answer to it. Only a body that does
    // carry a redacted field gets rewritten.
    private static string Redacted(string body)
    {
        if (TryRedactJson(body, out string? redactedJson))
        {
            return redactedJson;
        }

        // Not JSON, or JSON too broken to parse - which is itself worth
        // seeing, so it is still traced, through the patterns instead.
        return _redactedBodyPatterns.Aggregate(
            body,
            (current, pattern) => pattern.Replace(
                current,
                $"$1\"{Constants.SyncTrace.RedactedValue}\""));
    }

    // Null out when the body is not JSON, or is JSON with nothing to redact.
    // Working on the parsed document rather than the text is what catches a
    // field name spelled with escapes - "password" binds to `password`
    // just the same, and a pattern over the raw text would walk past it.
    private static bool TryRedactJson(string body, out string redacted)
    {
        redacted = string.Empty;

        JsonNode? root;

        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return false;
        }

        if (root is null || !RedactNode(root)) { return false; }

        redacted = root.ToJsonString();

        return true;
    }

    // True when anything was actually redacted.
    private static bool RedactNode(JsonNode node)
    {
        bool redactedAnything = false;

        switch (node)
        {
            case JsonObject jsonObject:
                foreach (string name in jsonObject.Select(property => property.Key).ToArray())
                {
                    if (IsRedactedBodyField(name))
                    {
                        jsonObject[name] = Constants.SyncTrace.RedactedValue;
                        redactedAnything = true;
                    }
                    else if (jsonObject[name] is JsonNode child)
                    {
                        redactedAnything |= RedactNode(child);
                    }
                }

                break;

            case JsonArray jsonArray:
                foreach (JsonNode? item in jsonArray)
                {
                    if (item is not null) { redactedAnything |= RedactNode(item); }
                }

                break;
        }

        return redactedAnything;
    }

    private static bool IsRedactedBodyField(string name) =>
        Constants.SyncTrace.RedactedBodyFields.Contains(name, StringComparer.OrdinalIgnoreCase);

    private string Truncated(string body)
    {
        int limit = _options.BodyCharacterLimit;

        if (body.Length <= limit) { return body; }

        return body[..limit] + Constants.SyncTrace.TruncationSuffix;
    }

    // Appends a section to the line, or nothing at all when there is nothing
    // to say - a trailing separator with empty space after it reads like
    // something went missing.
    private static string Section(string text) =>
        string.IsNullOrEmpty(text) ? string.Empty : Constants.SyncTrace.SectionSeparator + text;

    // A KOReader client names itself with x-auth-user on every protocol
    // request and the portal never sends that header, so it - not the path -
    // is what tells a device's traffic from a browser's. Recognising devices
    // this way is what lets the trace catch one asking for a path this
    // server does not have, which no path-based filter could.
    //
    // The protocol paths are traced regardless, so a device that sent no
    // credentials at all still appears on the paths it should have used.
    //
    // The gap, stated plainly: a client that sends neither header *and* asks
    // for a path outside that list is not traced. Closing it would mean
    // tracing everything that is not the portal, and buffering the
    // dashboard's own responses to find out. If a device ever has to be
    // chased that far, widen ProtocolPaths to the prefix it is using.
    private static bool IsClientRequest(HttpRequest request) =>
        request.Headers.ContainsKey(Constants.AuthHeaders.Username) ||
        request.Headers.ContainsKey(HeaderNames.Authorization) ||
        IsProtocolPath(request.Path);

    private static bool IsProtocolPath(PathString path) =>
        Constants.SyncTrace.ProtocolPaths.Any(protocolPath =>
            path.StartsWithSegments(protocolPath, StringComparison.OrdinalIgnoreCase));

    // Path and query together: the query string is where a client that
    // learned a different dialect of this protocol would be putting the
    // document hash, and an empty one costs nothing to print.
    private static string RequestTarget(HttpRequest request) =>
        $"{request.Path}{request.QueryString}";
}
