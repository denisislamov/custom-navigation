using System.Net;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using DotRecastServer;
using DotRecastServer.Navigation;

const string DefaultListenPrefix = "http://127.0.0.1:5079/";

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
};

Console.WriteLine("[startup] Loading the exported DotRecast artifact...");
string manifestPath = NavigationArtifactStore.ResolveManifestPath(args);
ServerNavigation navigation = NavigationArtifactStore.Load(manifestPath, jsonOptions);
string navigationDataDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))
                                 ?? AppContext.BaseDirectory;
string listenPrefix = ResolveListenPrefix(args);

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

Console.WriteLine(
    $"[ready] DotRecast 2026.1.3, level={navigation.LevelId}, " +
    $"artifact={navigation.ArtifactHash}, {navigation.PolygonCount} polygons, " +
    $"listening on {listenPrefix}");

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

        await HandleRequest(context, navigation, navigationDataDirectory, jsonOptions);
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
    ServerNavigation navigation,
    string navigationDataDirectory,
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
            await WriteJson(
                response,
                HttpStatusCode.OK,
                new HealthResponse(
                    "ok",
                    "2026.1.3",
                    navigation.PolygonCount,
                    navigation.LevelId,
                    navigation.Description,
                    navigation.ArtifactHash),
                jsonOptions);
            return;
        }

        if (request.HttpMethod == "GET" && path == "/artifacts")
        {
            await WriteJson(
                response,
                HttpStatusCode.OK,
                NavigationArtifactStore.ListArtifacts(navigationDataDirectory, navigation, jsonOptions),
                jsonOptions);
            return;
        }

        if (request.HttpMethod == "POST" && path == "/path")
        {
            PathRequest? pathRequest;
            try
            {
                pathRequest = await JsonSerializer.DeserializeAsync<PathRequest>(
                    request.InputStream,
                    jsonOptions);
            }
            catch (JsonException exception)
            {
                await WriteJson(
                    response,
                    HttpStatusCode.BadRequest,
                    new PathResponse(
                        false,
                        Array.Empty<Vector3Dto>(),
                        "Invalid JSON: " + exception.Message,
                        "invalid",
                        navigation.ArtifactHash,
                        string.Empty,
                        false),
                    jsonOptions);
                return;
            }

            if (pathRequest?.Start is null || pathRequest.Destination is null)
            {
                await WriteJson(
                    response,
                    HttpStatusCode.BadRequest,
                    new PathResponse(
                        false,
                        Array.Empty<Vector3Dto>(),
                        "Both start and destination are required.",
                        pathRequest?.RequestId ?? "invalid",
                        navigation.ArtifactHash,
                        string.Empty,
                        false),
                    jsonOptions);
                return;
            }

            long sequence = Interlocked.Increment(ref ServerLog.PathRequestSequence);
            string requestId = string.IsNullOrWhiteSpace(pathRequest.RequestId)
                ? sequence.ToString(CultureInfo.InvariantCulture)
                : pathRequest.RequestId;
            Console.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] [path {requestId}] " +
                $"input start={FormatPoint(pathRequest.Start)}, " +
                $"destination={FormatPoint(pathRequest.Destination)}, " +
                $"clientArtifact={pathRequest.ClientArtifactHash ?? "none"}, " +
                $"clientPath={pathRequest.ClientPathFingerprint ?? "none"}");

            var stopwatch = Stopwatch.StartNew();
            PathResponse pathResponse = navigation.FindPath(pathRequest);
            stopwatch.Stop();

            Console.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] [path {requestId}] " +
                $"output success={pathResponse.Success}, points={pathResponse.Points.Count}, " +
                $"elapsed={stopwatch.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)} ms, " +
                $"artifact={pathResponse.ArtifactHash}, fingerprint={pathResponse.PathFingerprint}, " +
                $"mismatch={pathResponse.ServerMismatchDetected}");
            for (int i = 0; i < pathResponse.Points.Count; i++)
            {
                Console.WriteLine(
                    $"[{DateTimeOffset.Now:HH:mm:ss.fff}] [path {requestId}] " +
                    $"output[{i}]={FormatPoint(pathResponse.Points[i])}");
            }
            Console.WriteLine(
                $"[{DateTimeOffset.Now:HH:mm:ss.fff}] [path {requestId}] " +
                $"message=\"{pathResponse.Message}\"");

            await WriteJson(
                response,
                HttpStatusCode.OK,
                pathResponse,
                jsonOptions);
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

static string FormatPoint(Vector3Dto point)
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

internal static class ServerLog
{
    public static long PathRequestSequence;
}
