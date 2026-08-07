using System.Net;

namespace DotRecastServer.Navigation;

/// <summary>
/// Decides who may push navmeshes with <c>POST /artifacts</c>.
///
/// Uploading rewrites the maps every client paths on, so it cannot be open to the
/// whole network. A server bound to loopback is only reachable from the same machine
/// and stays open for convenience; the moment it listens on a real interface a token
/// is required, and without one uploads are refused outright rather than silently
/// left wide open.
/// </summary>
public sealed class NavigationUploadPolicy
{
    public const string TokenHeader = "X-Navigation-Token";

    private readonly string? token;
    private readonly bool loopbackOnly;

    private NavigationUploadPolicy(string? token, bool loopbackOnly)
    {
        this.token = token;
        this.loopbackOnly = loopbackOnly;
    }

    public static NavigationUploadPolicy Resolve(string[] args, string listenPrefix)
    {
        string? token = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--upload-token", StringComparison.Ordinal))
            {
                continue;
            }

            if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                throw new ArgumentException("--upload-token requires a value.");
            }

            token = args[i + 1].Trim();
            break;
        }

        return new NavigationUploadPolicy(token, IsLoopbackPrefix(listenPrefix));
    }

    public bool IsAuthorized(HttpListenerRequest request, out string rejection)
    {
        rejection = string.Empty;

        if (token is not null)
        {
            string? provided = request.Headers[TokenHeader];
            if (string.Equals(provided, token, StringComparison.Ordinal))
            {
                return true;
            }

            rejection =
                $"Upload rejected: a valid '{TokenHeader}' header is required. " +
                "Set the same token in the Unity Server tab.";
            return false;
        }

        if (loopbackOnly)
        {
            return true;
        }

        rejection =
            "Uploads are disabled because this server is reachable from the network and " +
            "no upload token is set. Restart it with --upload-token <secret> and put the " +
            "same secret in the Unity Server tab.";
        return false;
    }

    public string Describe()
    {
        if (token is not null)
        {
            return $"POST /artifacts enabled, protected by the {TokenHeader} header.";
        }

        return loopbackOnly
            ? "POST /artifacts enabled for local connections only (loopback listen prefix)."
            : "POST /artifacts DISABLED: listening on a network interface without --upload-token.";
    }

    private static bool IsLoopbackPrefix(string listenPrefix)
    {
        if (!Uri.TryCreate(listenPrefix, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        // "http://*:5079/" and "http://+:5079/" parse with those characters as the host.
        string host = uri.Host;
        if (host is "*" or "+")
        {
            return false;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address);
    }
}

