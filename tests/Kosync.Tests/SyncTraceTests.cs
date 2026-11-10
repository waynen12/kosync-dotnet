using System.Net;
using System.Net.Http.Headers;
using Kosync.Models;
using Kosync.Tests.TestSupport;

namespace Kosync.Tests;

// The Sync Trace (issue #41 follow-up): what an operator turns on to see a
// device's actual requests and the answers it got.
//
// Asserted on the log output because that is the whole of what this feature
// produces - there is no response change to observe, by design.
public class SyncTraceTests : IntegrationTestBase
{
    protected override KosyncWebApplicationFactory CreateFactory() =>
        new() { SyncTraceEnabled = true };

    private IReadOnlyList<string> TraceLines =>
        Factory.Logs.Entries
            .Select(entry => entry.Message)
            .Where(message => message.Contains(Constants.SyncTrace.Marker))
            .ToList();

    private string RequestLine =>
        Assert.Single(TraceLines, line => line.Contains(Constants.SyncTrace.RequestArrow));

    private string ResponseLine =>
        Assert.Single(TraceLines, line => line.Contains(Constants.SyncTrace.ResponseArrow));

    [Fact]
    public async Task AProgressPull_TracesTheRequestLineAndTheAnswer()
    {
        await GetAsync("/syncs/progress/hash1", "admin", "admin");

        Assert.Contains("GET", RequestLine);
        Assert.Contains("/syncs/progress/hash1", RequestLine);
        Assert.Contains("200", ResponseLine);
    }

    // Which device is asking is half the diagnosis, so the headers go in the
    // trace - but x-auth-key is the credential itself, and a log file is not
    // where it belongs. Redacted rather than dropped: "it sent a key we are
    // not printing" and "it sent no key" are different faults.
    [Fact]
    public async Task TheAuthKeyHeader_IsRedactedAndTheUsernameIsNot()
    {
        await GetAsync("/syncs/progress/hash1", "admin", "admin");

        Assert.Contains($"{Constants.AuthHeaders.Username}: admin", RequestLine);
        Assert.Contains(
            $"{Constants.AuthHeaders.AuthKey}: {Constants.SyncTrace.RedactedValue}",
            RequestLine);
        Assert.DoesNotContain(Utility.Md5Hash("admin"), RequestLine);
    }

    // The header a device identifies its software with - it is how we would
    // know whether this is the reader in the bug report at all.
    [Fact]
    public async Task AnArbitraryRequestHeader_IsTraced()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/syncs/progress/hash1");
        request.SetCredentials("admin", "admin");
        request.Headers.Add("user-agent", "CrossPoint/1.0");

        await Client.SendAsync(request);

        // Case-insensitively, because the casing in the trace is whatever
        // the client sent - header names are case-insensitive on the wire and
        // the trace prints them verbatim rather than tidying them up.
        Assert.Contains(
            "user-agent: CrossPoint/1.0",
            RequestLine,
            StringComparison.OrdinalIgnoreCase);
    }

    // The field names a client actually sent are the whole reason to trace a
    // push: a device using a different spelling for any of them binds to a
    // default and pushes nonsense, and nothing in the stored row says so.
    [Fact]
    public async Task AProgressPush_TracesItsRequestBody()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.25m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        Assert.Contains("\"document\":\"hash1\"", RequestLine);
        Assert.Contains("\"device_id\":\"device-1\"", RequestLine);
    }

    // Tracing means reading the request body before the endpoint does, and
    // a body can only be read once unless it is buffered. Get that wrong and
    // turning the trace on breaks every push - so this asserts the push
    // worked, through the interface, rather than that a line was logged.
    [Fact]
    public async Task TracingAPush_LeavesThePushWorking()
    {
        var push = await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.25m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        Assert.Equal(HttpStatusCode.OK, push.StatusCode);

        var pull = await GetAsync("/syncs/progress/hash1", "admin", "admin");
        var body = await BodyAsync(pull);

        Assert.Equal("p1", body.GetProperty("progress").GetString());
        Assert.Equal(0.25m, body.GetProperty("percentage").GetDecimal());
    }

    // What we told the device, in the device's own terms. The answer being
    // the thing under suspicion is the entire premise of issue #41.
    [Fact]
    public async Task TheResponseBody_IsTracedAndStillReachesTheClient()
    {
        var response = await GetAsync("/syncs/progress/does-not-exist", "admin", "admin");

        Assert.Contains(Constants.SyncProtocol.EmptyProgressBody, ResponseLine);
        Assert.Equal(
            Constants.SyncProtocol.EmptyProgressBody,
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ARequestWithNoBody_SaysSoRatherThanTrailingOff()
    {
        await GetAsync("/syncs/progress/hash1", "admin", "admin");

        Assert.Contains(Constants.SyncTrace.EmptyBodyText, RequestLine);
    }

    // A password reaches the server in clear text on this one endpoint, and
    // the trace must not be the thing that writes it down.
    [Fact]
    public async Task APasswordInABody_IsRedacted()
    {
        await PostAsync("/manage/users", new UserCreateRequest
        {
            username = "reader",
            password = "hunter2"
        }, "admin", "admin");

        Assert.DoesNotContain("hunter2", string.Join("\n", TraceLines));

        // The field itself, not just "something was redacted" - the auth key
        // header is redacted on this line too, so a bare check for the
        // placeholder would pass with the password written out in full.
        Assert.Contains($"\"password\":\"{Constants.SyncTrace.RedactedValue}\"", RequestLine);
        Assert.Contains("\"username\":\"reader\"", RequestLine);
    }

    // Content-Type on the way out, because "does the client reject the
    // answer it was given" is the next question after issue #41 and the
    // status code alone cannot answer it.
    [Fact]
    public async Task TheResponseHeaders_AreTraced()
    {
        await GetAsync("/syncs/progress/hash1", "admin", "admin");

        Assert.Contains(
            Constants.SyncProtocol.ProgressContentType,
            ResponseLine,
            StringComparison.OrdinalIgnoreCase);
    }

    // Two devices syncing at once interleave in the log, so the pair has to
    // be matchable by something other than adjacency.
    [Fact]
    public async Task TheRequestAndItsAnswer_ShareATraceIdentifier()
    {
        await GetAsync("/syncs/progress/hash1", "admin", "admin");

        var identifier = RequestLine
            .Split(Constants.SyncTrace.RequestArrow)[0]
            .Split(Constants.SyncTrace.Marker)[1]
            .Trim();

        Assert.NotEmpty(identifier);
        Assert.Contains(identifier, ResponseLine);
    }

    // The redaction works on the parsed body, not its text, so a field name
    // spelled with a JSON escape is caught too - it binds to `password` on
    // the server all the same.
    [Fact]
    public async Task APasswordUnderAnEscapedFieldName_IsStillRedacted()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/manage/users")
        {
            Content = new StringContent(
                "{\"username\":\"reader\",\"\\u0070assword\":\"hunter2\"}",
                new MediaTypeHeaderValue(Constants.SyncProtocol.ProgressContentType))
        };
        request.SetCredentials("admin", "admin");

        await Client.SendAsync(request);

        Assert.DoesNotContain("hunter2", string.Join("\n", TraceLines));
    }

    // A form-encoded body is not something this protocol uses, which is
    // exactly why it reaches the trace before anything rejects it - so it
    // cannot be the hole the redaction leaks through.
    [Fact]
    public async Task APasswordInAFormEncodedBody_IsStillRedacted()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, AppRoutes.UsersCreate)
        {
            Content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("username", "reader"),
                new KeyValuePair<string, string>("password", "hunter2"),
            ])
        };

        await Client.SendAsync(request);

        Assert.DoesNotContain("hunter2", string.Join("\n", TraceLines));
    }

    // A body with nothing sensitive in it is logged exactly as it arrived.
    // The trace exists to answer "what did the device actually send", and a
    // body we tidied up on the way past is not an answer to that.
    [Fact]
    public async Task AnOrdinaryBody_IsTracedVerbatim()
    {
        const string body = """{ "document" : "hash1", "progress": "p1", "percentage": 0.25, "device": "Kobo", "device_id": "device-1" }""";

        var request = new HttpRequestMessage(HttpMethod.Put, AppRoutes.SyncsProgress)
        {
            Content = new StringContent(
                body,
                new MediaTypeHeaderValue(Constants.SyncProtocol.ProgressContentType))
        };
        request.SetCredentials("admin", "admin");

        await Client.SendAsync(request);

        Assert.Contains(body, RequestLine);
    }

    // The case the whole feature exists for: a device asking for a path this
    // server does not serve gets a 404 that no controller action ever sees,
    // so today it leaves no trace at all. Recognised as a device by its
    // x-auth-user header rather than by its path, which is what makes an
    // unknown path traceable.
    [Fact]
    public async Task ARequestToAPathWeDoNotServe_IsTracedWithIts404()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/koreader/syncs/progress/hash1");
        request.SetCredentials("admin", "admin");

        await Client.SendAsync(request);

        Assert.Contains("/koreader/syncs/progress/hash1", RequestLine);
        Assert.Contains("404", ResponseLine);
    }

    // A verb we do not accept is the same class of silent failure: 405 from
    // routing, nothing in the log.
    [Fact]
    public async Task ARequestWithAVerbWeDoNotAccept_IsTracedWithIts405()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/syncs/progress/hash1");
        request.SetCredentials("admin", "admin");

        await Client.SendAsync(request);

        Assert.Contains("405", ResponseLine);
    }

    // The portal is a browser talking cookies, not a device talking
    // x-auth-user. Tracing it would bury the one exchange an operator turned
    // this on to read under the dashboard's own traffic.
    [Fact]
    public async Task APortalRequest_IsNotTraced()
    {
        await Client.GetAsync(AppRoutes.Login);

        Assert.Empty(TraceLines);
    }
}

// A trace on a server with a surprise payload coming at it should not be
// the thing that fills the disk, so how much of a body gets written is the
// operator's to set.
public class SyncTraceBodyLimitTests : IntegrationTestBase
{
    private const int Limit = 20;

    protected override KosyncWebApplicationFactory CreateFactory() =>
        new() { SyncTraceEnabled = true, SyncTraceMaxBodyCharacters = Limit };

    [Fact]
    public async Task ABodyOverTheConfiguredLimit_IsCutOffAndSaysSo()
    {
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = new string('p', 200),
            percentage = 0.25m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        var requestLine = Assert.Single(
            Factory.Logs.Entries.Select(entry => entry.Message),
            message => message.Contains(Constants.SyncTrace.Marker) && message.Contains("-->"));

        Assert.Contains(Constants.SyncTrace.TruncationSuffix, requestLine);
        Assert.DoesNotContain(new string('p', Limit + 1), requestLine);
    }
}

// Split from SyncTraceTests because its subject is the shipped default
// rather than a trace that is on: the setting binds at startup, so "off
// unless asked for" can only be shown by a host that was never asked.
public class SyncTraceOffByDefaultTests : IntegrationTestBase
{
    [Fact]
    public async Task WithNothingConfigured_NothingIsTraced()
    {
        await GetAsync("/syncs/progress/hash1", "admin", "admin");
        await PutAsync("/syncs/progress", new DocumentRequest
        {
            document = "hash1",
            progress = "p1",
            percentage = 0.25m,
            device = "Kobo",
            device_id = "device-1"
        }, "admin", "admin");

        Assert.DoesNotContain(
            Constants.SyncTrace.Marker,
            string.Join("\n", Factory.Logs.Entries.Select(entry => entry.Message)));
    }
}
