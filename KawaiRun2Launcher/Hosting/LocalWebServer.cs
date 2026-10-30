using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace KawaiRun2Launcher.Hosting;

internal sealed class LocalWebServer : IAsyncDisposable
{
    public Action? SwitchRequested { get; set; }

    public const int PreferredPort = 47820;
    public const int LastPort = 47829;
    public const string SiteOrigin = "https://kawairun2.puzzlebest.tech";
    private const string SiteHost = "kawairun2.puzzlebest.tech";

    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Connection", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailer", "Transfer-Encoding", "Upgrade"
    };

    private static readonly HttpClient UpstreamClient = CreateUpstreamClient();

    private readonly WebApplication _app;
    private readonly string _distDir;
    private readonly string _injectedScript;
    private readonly string _localOrigin;

    public int Port { get; }
    public string Url => _localOrigin + "/";

    private LocalWebServer(WebApplication app, string distDir, string injectedScript, int port)
    {
        _app = app;
        _distDir = distDir;
        _injectedScript = injectedScript;
        Port = port;
        _localOrigin = $"http://127.0.0.1:{port}";
    }

    public static async Task<LocalWebServer> StartAsync(string distDir, string version, string platform)
    {
        string script = "<script>window.__KR_SITE_PROXY=true;window.__KR_NATIVE={version:" +
                        JsonSerializer.Serialize(version) + ",platform:" + JsonSerializer.Serialize(platform) +
                        "};</script>";

        Exception? last = null;
        for (int port = PreferredPort; port <= LastPort; port++)
        {
            WebApplication app = BuildApp(distDir, port);
            LocalWebServer server = new(app, distDir, script, port);
            server.Configure();
            try
            {
                await app.StartAsync();
                return server;
            }
            catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or InvalidOperationException)
            {
                last = ex;
                try { await app.DisposeAsync(); } catch { }
            }
        }

        throw new LauncherException(
            "Local Server Error",
            $"Could not open a local port in the range {PreferredPort}-{LastPort}.\n\n{last?.Message}");
    }

    private static WebApplication BuildApp(string distDir, int port)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = distDir,
            WebRootPath = distDir
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 100L * 1024 * 1024;
            options.Listen(IPAddress.Loopback, port);
        });
        return builder.Build();
    }

    private void Configure()
    {
        FileExtensionContentTypeProvider mime = new();
        mime.Mappings[".webp"] = "image/webp";
        mime.Mappings[".mp3"] = "audio/mpeg";
        mime.Mappings[".json"] = "application/json";
        mime.Mappings[".ttf"] = "font/ttf";
        mime.Mappings[".wasm"] = "application/wasm";
        mime.Mappings[".js"] = "text/javascript";
        mime.Mappings[".mjs"] = "text/javascript";
        mime.Mappings[".css"] = "text/css";
        mime.Mappings[".html"] = "text/html";
        mime.Mappings[".map"] = "application/json";

        _app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

        _app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/__kr/log" && HttpMethods.IsPost(context.Request.Method))
            {
                await AppendGameLogAsync(context);
                return;
            }
            if (context.Request.Path == "/__kr/switch" && HttpMethods.IsPost(context.Request.Method))
            {
                if (context.Request.Headers.Origin == _localOrigin)
                {
                    SwitchRequested?.Invoke();
                }
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            await next(context);
        });

        _app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            {
                PathString path = context.Request.Path;
                if (path == "/" || !path.HasValue)
                {
                    path = "/port.html";
                    context.Request.Path = path;
                }

                if (path.Value!.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                {
                    IFileInfo file = _app.Environment.WebRootFileProvider.GetFileInfo(path.Value);
                    if (file.Exists && !file.IsDirectory)
                    {
                        await ServeHtmlAsync(context, file);
                        return;
                    }
                }
            }

            await next(context);
        });

        _app.UseStaticFiles(new StaticFileOptions
        {
            ContentTypeProvider = mime,
            ServeUnknownFileTypes = false,
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
        });

        _app.Run(ProxyAsync);
    }

    private static readonly SemaphoreSlim GameLogLock = new(1, 1);
    private const int GameLogLineLimit = 4096;
    private const long GameLogMaxBytes = 1024 * 1024;

    private static async Task AppendGameLogAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        string text;
        using (StreamReader reader = new(context.Request.Body, Encoding.UTF8))
        {
            char[] buffer = new char[GameLogLineLimit];
            int read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            text = new string(buffer, 0, read).Replace("\r", " ").Replace("\n", " | ");
        }
        if (text.Length == 0) return;

        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KawaiRun2");
        string file = Path.Combine(dir, "game.log");
        await GameLogLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(dir);
            FileInfo info = new(file);

            if (info.Exists && info.Length > GameLogMaxBytes) File.Move(file, file + ".old", true);
            await File.AppendAllTextAsync(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        finally
        {
            GameLogLock.Release();
        }
    }

    private async Task ServeHtmlAsync(HttpContext context, IFileInfo file)
    {
        string html;
        await using (Stream stream = file.CreateReadStream())
        using (StreamReader reader = new(stream, Encoding.UTF8))
        {
            html = await reader.ReadToEndAsync();
        }

        int index = html.IndexOf("<script", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            index = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        }
        html = index >= 0 ? html.Insert(index, _injectedScript) : _injectedScript + html;

        byte[] body = Encoding.UTF8.GetBytes(html);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.ContentLength = body.Length;
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.Body.WriteAsync(body, context.RequestAborted);
        }
    }

    private async Task ProxyAsync(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest && context.Request.Path.StartsWithSegments("/ws"))
        {
            await ProxyWebSocketAsync(context);
            return;
        }

        string target = SiteOrigin + (context.Features.Get<IHttpRequestFeature>()?.RawTarget
                                      ?? context.Request.Path.ToUriComponent() + context.Request.QueryString);

        using HttpRequestMessage request = new(new HttpMethod(context.Request.Method), target);

        bool hasBody = !HttpMethods.IsGet(context.Request.Method) &&
                       !HttpMethods.IsHead(context.Request.Method) &&
                       !HttpMethods.IsTrace(context.Request.Method) &&
                       (context.Request.ContentLength is > 0 ||
                        context.Request.Headers.ContainsKey("Transfer-Encoding") ||
                        HttpMethods.IsPost(context.Request.Method) ||
                        HttpMethods.IsPut(context.Request.Method) ||
                        HttpMethods.IsPatch(context.Request.Method));
        if (hasBody)
        {
            request.Content = new StreamContent(context.Request.Body);
        }

        foreach (KeyValuePair<string, StringValues> header in context.Request.Headers)
        {
            string name = header.Key;
            if (HopByHopHeaders.Contains(name) ||
                name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Origin", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Referer", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!request.Headers.TryAddWithoutValidation(name, (IEnumerable<string>)header.Value) &&
                request.Content is not null)
            {
                request.Content.Headers.TryAddWithoutValidation(name, (IEnumerable<string>)header.Value);
            }
        }

        if (request.Content is not null && context.Request.ContentLength is long length)
        {
            request.Content.Headers.ContentLength = length;
        }

        request.Headers.Host = SiteHost;
        request.Headers.TryAddWithoutValidation("Origin", SiteOrigin);
        if (context.Request.Headers.TryGetValue("Referer", out StringValues referer))
        {
            request.Headers.TryAddWithoutValidation("Referer", referer.ToString().Replace(_localOrigin, SiteOrigin));
        }

        HttpResponseMessage response;
        try
        {
            response = await UpstreamClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("Upstream error: " + ex.Message);
            }
            return;
        }

        using (response)
        {
            context.Response.StatusCode = (int)response.StatusCode;

            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers)
            {
                CopyResponseHeader(context, header.Key, header.Value);
            }
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Content.Headers)
            {
                CopyResponseHeader(context, header.Key, header.Value);
            }

            if (HttpMethods.IsHead(context.Request.Method))
            {
                return;
            }

            try
            {
                await using Stream upstream = await response.Content.ReadAsStreamAsync(context.RequestAborted);
                await upstream.CopyToAsync(context.Response.Body, 81920, context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                context.Abort();
            }
        }
    }

    private void CopyResponseHeader(HttpContext context, string name, IEnumerable<string> values)
    {
        if (HopByHopHeaders.Contains(name) || name.Equals("Server", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string cookie in values)
            {
                context.Response.Headers.Append(name, StripCookieDomain(cookie));
            }
            return;
        }

        if (name.Equals("Location", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers[name] = values.Select(v => v.Replace(SiteOrigin, _localOrigin)).ToArray();
            return;
        }

        context.Response.Headers[name] = values.ToArray();
    }

    private static string StripCookieDomain(string cookie)
    {
        string[] parts = cookie.Split(';');
        return string.Join(";", parts.Where(p => !p.TrimStart().StartsWith("Domain=", StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task ProxyWebSocketAsync(HttpContext context)
    {
        using ClientWebSocket upstream = new();
        upstream.Options.SetRequestHeader("Origin", SiteOrigin);
        upstream.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        if (context.Request.Headers.TryGetValue("User-Agent", out StringValues userAgent))
        {
            upstream.Options.SetRequestHeader("User-Agent", userAgent.ToString());
        }

        string? subProtocol = null;
        foreach (string protocol in context.WebSockets.WebSocketRequestedProtocols)
        {
            upstream.Options.AddSubProtocol(protocol);
        }

        Uri upstreamUri = new("wss://" + SiteHost + context.Request.Path.ToUriComponent() + context.Request.QueryString);
        try
        {
            using CancellationTokenSource connectCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            connectCts.CancelAfter(TimeSpan.FromSeconds(20));
            await upstream.ConnectAsync(upstreamUri, connectCts.Token);
            subProtocol = upstream.SubProtocol;
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync("Upstream websocket error: " + ex.Message);
            return;
        }

        using WebSocket client = await context.WebSockets.AcceptWebSocketAsync(subProtocol);
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

        Task a = PumpAsync(client, upstream, cts);
        Task b = PumpAsync(upstream, client, cts);
        await Task.WhenAny(a, b);
        cts.Cancel();
        try { await Task.WhenAll(a, b); } catch { }
    }

    private static async Task PumpAsync(WebSocket from, WebSocket to, CancellationTokenSource cts)
    {
        byte[] buffer = new byte[64 * 1024];
        try
        {
            while (!cts.IsCancellationRequested && from.State == WebSocketState.Open)
            {
                ValueWebSocketReceiveResult result = await from.ReceiveAsync(buffer.AsMemory(), cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (to.State is WebSocketState.Open or WebSocketState.CloseReceived)
                    {
                        await to.CloseOutputAsync(
                            from.CloseStatus is null or WebSocketCloseStatus.Empty
                                ? WebSocketCloseStatus.NormalClosure
                                : from.CloseStatus.Value,
                            from.CloseStatusDescription,
                            CancellationToken.None);
                    }
                    break;
                }

                if (to.State != WebSocketState.Open)
                {
                    break;
                }
                await to.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, cts.Token);
            }
        }
        catch
        {
        }
        finally
        {
            cts.Cancel();
        }
    }

    private static HttpClient CreateUpstreamClient()
    {
        SocketsHttpHandler handler = new()
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _app.StopAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        await _app.DisposeAsync();
    }
}
