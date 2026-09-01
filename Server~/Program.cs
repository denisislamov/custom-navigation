using System.Net;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using CustomNavigation.Runtime;
using DotRecastServer;
using DotRecastServer.Navigation;

const string DefaultListenPrefix = "http://127.0.0.1:5079/";

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
};

CanonicalJitterContract.ValidateInstalledFiles(Directory.GetFiles(
    AppContext.BaseDirectory,
    "Jitter2.Core.dll",
    SearchOption.AllDirectories));

Console.WriteLine("[startup] Loading the exported DotRecast artifact...");
string navigationDataDirectory = NavigationArtifactStore.ResolveDataDirectory(args);
string? pinnedManifest = NavigationArtifactStore.ResolvePinnedManifestPath(args);
var registry = new NavigationRegistry(navigationDataDirectory, pinnedManifest, jsonOptions);
string listenPrefix = ResolveListenPrefix(args);
var uploadPolicy = NavigationUploadPolicy.Resolve(args, listenPrefix);

using var listener = new HttpListener();
listener.Prefixes.Add(listenPrefix);
listener.Start();

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
    listener.Stop();
};

// A missing artifact is a normal first-run state, not a fatal error: the server has to
// be running before Unity can export to it. It reports the problem over /health and
// picks the artifact up on the next request, without a restart.
if (registry.TryResolve(null, out ServerNavigation? startupNavigation, out string startupError)
    && startupNavigation is not null)
{
    Console.WriteLine(
        $"[ready] DotRecast 2026.1.3, level={startupNavigation.LevelId}, " +
        $"artifact={startupNavigation.ArtifactHash}, {startupNavigation.PolygonCount} polygons, " +
        $"listening on {listenPrefix}");
}
else
{
    Console.WriteLine($"[waiting] {startupError}");
    Console.WriteLine(
        $"[ready] DotRecast 2026.1.3, no artifact loaded yet, data={navigationDataDirectory}, " +
        $"listening on {listenPrefix}");
}

Console.WriteLine($"[upload] {uploadPolicy.Describe()}");

try
{
    while (!shutdown.IsCancellationRequested)
    {
        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync();
        }
        catch (HttpListenerException) when (shutdown.IsCancellationRequested)
        {
            break;
        }
        catch (ObjectDisposedException) when (shutdown.IsCancellationRequested)
        {
            break;
        }

        await HandleRequest(context, registry, uploadPolicy, jsonOptions);
    }
}
finally
{
    if (listener.IsListening)
    {
        listener.Stop();
    }
}

static async Task HandleRequest(
    HttpListenerContext context,
    NavigationRegistry registry,
    NavigationUploadPolicy uploadPolicy,
    JsonSerializerOptions jsonOptions)
{
    HttpListenerRequest request = context.Request;
    HttpListenerResponse response = context.Response;
    response.Headers["Access-Control-Allow-Origin"] = "*";
    response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
    response.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";

    try
    {
        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = (int)HttpStatusCode.NoContent;
            return;
        }

        string path = request.Url?.AbsolutePath ?? string.Empty;
        if (request.HttpMethod == "GET" && path == "/health")
        {
            bool resolved = registry.TryResolve(
                request.QueryString["level"],
                out ServerNavigation? navigation,
                out string healthError);

            await WriteJson(
                response,
                HttpStatusCode.OK,
                resolved && navigation is not null
                    ? new HealthResponse(
                        "ok",
                        "2026.1.3",
                        navigation.PolygonCount,
                        navigation.LevelId,
                        navigation.Description,
                        navigation.ArtifactHash,
                        string.Empty,
                        registry.DataDirectory,
                        registry.AvailableLevelIds())
                    : new HealthResponse(
                        "no-artifact",
                        "2026.1.3",
                        0,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        healthError,
                        registry.DataDirectory,
                        registry.AvailableLevelIds()),
                jsonOptions);
            return;
        }

        if (request.HttpMethod == "GET" && path == "/artifacts")
        {
            await WriteJson(
                response,
                HttpStatusCode.OK,
                NavigationArtifactStore.ListArtifacts(
                    registry.DataDirectory,
                    registry.TryGetActive(),
                    jsonOptions),
                jsonOptions);
            return;
        }

        if (request.HttpMethod == "POST" && path == "/artifacts")
        {
            if (!uploadPolicy.IsAuthorized(request, out string uploadRejection))
            {
                Console.WriteLine($"[upload] Denied: {uploadRejection}");
                await WriteJson(
                    response,
                    HttpStatusCode.Forbidden,
                    new ArtifactUploadResponse(
                        false,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        false,
                        uploadRejection),
                    jsonOptions);
                return;
            }

            ArtifactUploadRequest? upload;
            try
            {
                upload = await JsonSerializer.DeserializeAsync<ArtifactUploadRequest>(
                    request.InputStream,
                    jsonOptions);
            }
            catch (JsonException exception)
            {
                await WriteJson(
                    response,
                    HttpStatusCode.BadRequest,
                    new ArtifactUploadResponse(
                        false,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        false,
                        "Invalid JSON: " + exception.Message),
                    jsonOptions);
                return;
            }

            if (upload is null)
            {
                await WriteJson(
                    response,
                    HttpStatusCode.BadRequest,
                    new ArtifactUploadResponse(
                        false,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        false,
                        "Empty upload body."),
                    jsonOptions);
                return;
            }

            ArtifactUploadResponse uploadResult = NavigationArtifactStore.Save(
                registry.DataDirectory,
                upload,
                jsonOptions);

            // No reload needed: the registry notices the new manifest timestamp itself.
            await WriteJson(
                response,
                uploadResult.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest,
                uploadResult,
                jsonOptions);
            return;
        }

        if (request.HttpMethod == "POST" && path == "/path")
        {
            NavigationPathRequest pathRequest;
            try
            {
                using var bodyReader = new StreamReader(
                    request.InputStream,
                    System.Text.Encoding.UTF8,
                    true,
                    4096,
                    leaveOpen: true);
                pathRequest = NavigationWireCodec.DecodeRequest(await bodyReader.ReadToEndAsync());
            }
            catch (NavigationWireFormatException exception)
            {
                await WritePathJson(
                    response,
                    HttpStatusCode.BadRequest,
                    new NavigationPathResponse
                    {
                        Success = false,
                        Message = exception.Code + ": " + exception.Message,
                        RequestId = "invalid"
                    });
                return;
            }

            long sequence = Interlocked.Increment(ref ServerLog.PathRequestSequence);
            string requestId = string.IsNullOrWhiteSpace(pathRequest.RequestId)
                ? sequence.ToString(CultureInfo.InvariantCulture)
                : pathRequest.RequestId;

            if (!registry.TryResolve(
                    pathRequest.LevelId,
                    out ServerNavigation? navigation,
                    out string resolveError)
                || navigation is null)
            {
                Console.WriteLine(
                    $"[{DateTimeOffset.Now:HH:mm:ss.fff}] [path {requestId}] rejected: {resolveError}");

                // Deliberately 200 with success=false: the Unity client surfaces the
                // message only for a successful HTTP exchange, and this message is the
                // actionable part ("export from Unity first").
                await WritePathJson(
                    response,
                    HttpStatusCode.OK,
                    new NavigationPathResponse
                    {
                        Success = false,
                        Message = resolveError,
                        RequestId = requestId
                    });
                return;
            }

            Console.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] [path {requestId}] " +
                $"input level={navigation.LevelId}, start={FormatPoint(pathRequest.Start)}, " +
                $"destination={FormatPoint(pathRequest.Destination)}, " +
                $"clientArtifact={pathRequest.ClientArtifactHash ?? "none"}, " +
                $"clientPath={pathRequest.ClientPathFingerprint ?? "none"}");

            var stopwatch = Stopwatch.StartNew();
            NavigationPathResponse pathResponse = navigation.FindPath(pathRequest);
            stopwatch.Stop();

            Console.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] [path {requestId}] " +
                $"output success={pathResponse.Success}, points={pathResponse.Points.Length}, " +
                $"elapsed={stopwatch.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)} ms, " +
                $"artifact={pathResponse.ArtifactHash}, fingerprint={pathResponse.PathFingerprint}, " +
                $"mismatch={pathResponse.ServerMismatchDetected}");
            for (int i = 0; i < pathResponse.Points.Length; i++)
            {
                Console.WriteLine(
                    $"[{DateTimeOffset.Now:HH:mm:ss.fff}] [path {requestId}] " +
                    $"output[{i}]={FormatPoint(pathResponse.Points[i])}");
            }
            Console.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] [path {requestId}] " +
                $"message=\"{pathResponse.Message}\"");

            await WritePathJson(
                response,
                HttpStatusCode.OK,
                pathResponse);
            return;
        }

        await WriteJson(
            response,
            HttpStatusCode.NotFound,
            new { error = "Endpoint not found." },
            jsonOptions);
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception);
        if (!response.OutputStream.CanWrite)
        {
            return;
        }

        await WriteJson(
            response,
            HttpStatusCode.InternalServerError,
            new { error = "Internal server error." },
            jsonOptions);
    }
    finally
    {
        response.Close();
    }
}

static string FormatPoint(Jitter2.LinearMath.JVector point)
{
    return FormattableString.Invariant($"({point.X:F3}, {point.Y:F3}, {point.Z:F3})");
}

static string ResolveListenPrefix(string[] commandLineArguments)
{
    string prefix = DefaultListenPrefix;
    for (int i = 0; i < commandLineArguments.Length; i++)
    {
        if (!string.Equals(commandLineArguments[i], "--listen", StringComparison.Ordinal))
        {
            continue;
        }

        if (i + 1 >= commandLineArguments.Length
            || string.IsNullOrWhiteSpace(commandLineArguments[i + 1]))
        {
            throw new ArgumentException("--listen requires an HTTP prefix, for example http://*:5079/.");
        }

        prefix = commandLineArguments[i + 1].Trim();
        break;
    }

    if (!prefix.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
    {
        throw new ArgumentException("The navigation server currently supports only http:// listen prefixes.");
    }

    return prefix.EndsWith("/", StringComparison.Ordinal) ? prefix : prefix + "/";
}

static async Task WriteJson<T>(
    HttpListenerResponse response,
    HttpStatusCode statusCode,
    T value,
    JsonSerializerOptions jsonOptions)
{
    response.StatusCode = (int)statusCode;
    response.ContentType = "application/json; charset=utf-8";
    await JsonSerializer.SerializeAsync(response.OutputStream, value, jsonOptions);
}

static async Task WritePathJson(
    HttpListenerResponse response,
    HttpStatusCode statusCode,
    NavigationPathResponse value)
{
    response.StatusCode = (int)statusCode;
    response.ContentType = "application/json; charset=utf-8";
    byte[] payload = System.Text.Encoding.UTF8.GetBytes(NavigationWireCodec.EncodeResponse(value));
    await response.OutputStream.WriteAsync(payload);
}

internal static class ServerLog
{
    public static long PathRequestSequence;
}
