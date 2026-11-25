using System.Net;
using System.Net.Sockets;

namespace Kosync.Models;

// Bound from the "ForwardedHeaders" configuration section: which networks'
// X-Forwarded-Proto and X-Forwarded-For this server trusts enough to rewrite
// Request.Scheme and the connecting address with (issue #52).
//
// Not TRUSTED_PROXIES (Services/ProxyService.cs). That is a separate,
// hand-rolled mechanism the Sync Trace and ManagementController use to decide
// whose address to print - it reads a header's *value* after the fact and
// never touches Request.Scheme. This decides whether ASP.NET Core's own
// ForwardedHeadersMiddleware rewrites the request at all. The two lists have
// no reason to agree and are read independently.
//
// KnownNetworks defaults to empty, which is "trust nothing": the request's
// own scheme and connection address pass through exactly as they did before
// this issue. An operator opts in by naming the proxy's own container
// network (the droplet compose's subnet), never by widening this to
// something permissive by default - another container sharing that network
// (n8n, ai-hub, Postgres) could otherwise forge a client address or scheme
// into the Sync Trace and ManagementController.
public class ForwardedHeadersSettings
{
    public string? KnownNetworks { get; set; }

    public IReadOnlyList<IPNetwork> ParsedKnownNetworks
    {
        get
        {
            if (string.IsNullOrWhiteSpace(KnownNetworks))
            {
                return [];
            }

            List<IPNetwork> networks = [];

            foreach (var entry in KnownNetworks.Split(
                ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryParseNetwork(entry, out var network))
                {
                    networks.Add(network);
                }
            }

            return networks;
        }
    }

    // Accepts CIDR notation ("10.0.0.0/24") the way an operator would name a
    // compose network's subnet, and a bare address as the one-host network it
    // implies - the same two spellings TRUSTED_PROXIES accepts, for the same
    // reason: a single proxy's address is still a network of one host.
    private static bool TryParseNetwork(string value, out IPNetwork network)
    {
        if (IPNetwork.TryParse(value, out network))
        {
            return true;
        }

        if (IPAddress.TryParse(value, out var address))
        {
            int prefixLength = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            network = new IPNetwork(address, prefixLength);
            return true;
        }

        network = default;
        return false;
    }
}
