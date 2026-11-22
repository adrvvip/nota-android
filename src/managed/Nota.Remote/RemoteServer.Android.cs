// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Remote on Android: ASP.NET Core (Kestrel) does not exist there, so the same server is
// built on System.Net.HttpListener — every interface on the port, the phone app from embedded
// resources, the WebSocket at /ws. Same surface as RemoteServer.cs.

using System.Net;
using System.Reflection;

namespace Nota.Remote;

public sealed class RemoteServer : IAsyncDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private static readonly Assembly Asm = typeof(RemoteServer).Assembly;

    public bool Running => _listener is not null;
    public int Port { get; private set; }

    /// <summary>Listen on <paramref name="port"/> on all interfaces. Throws when the port is taken.</summary>
    public Task StartAsync(RemoteHub hub, int port)
    {
        if (_listener is not null) return Task.CompletedTask;
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://+:{port}/");
        listener.Start();
        var cts = new CancellationTokenSource();
        _listener = listener;
        _cts = cts;
        Port = port;
        _loop = Task.Run(() => AcceptLoopAsync(listener, hub, cts.Token));
        return Task.CompletedTask;
    }

    private static async Task AcceptLoopAsync(HttpListener listener, RemoteHub hub, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { if (ct.IsCancellationRequested || !listener.IsListening) return; continue; }
            _ = Task.Run(() => HandleAsync(ctx, hub, ct), ct);
        }
    }

    private static async Task HandleAsync(HttpListenerContext ctx, RemoteHub hub, CancellationToken ct)
    {
        try
        {
            ctx.Response.Headers.Remove("Server");
            string path = ctx.Request.Url?.AbsolutePath ?? "/";
            if (path == "/ws")
            {
                if (!ctx.Request.IsWebSocketRequest) { ctx.Response.StatusCode = 400; ctx.Response.Close(); return; }
                var wsc = await ctx.AcceptWebSocketAsync(null, TimeSpan.FromSeconds(5));
                using var ws = wsc.WebSocket;
                string addr = ctx.Request.RemoteEndPoint?.Address.ToString() ?? "?";
                await hub.RunClientAsync(ws, addr, ct);
                return;
            }
            await ServeStaticAsync(ctx, path);
        }
        catch
        {
            try { ctx.Response.Abort(); } catch { }
        }
    }

    public async Task StopAsync()
    {
        if (_listener is not { } l) return;
        _listener = null;
        _cts?.Cancel();
        try { l.Stop(); l.Close(); } catch { }
        if (_loop is { } loop) { try { await loop.WaitAsync(TimeSpan.FromSeconds(1)); } catch { } }
        _loop = null;
        _cts?.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    // ---- the phone app, from embedded resources ---------------------------------------

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json",
        [".webmanifest"] = "application/manifest+json",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".woff2"] = "font/woff2",
        [".mp4"] = "video/mp4",
        [".ico"] = "image/x-icon",
    };

    private static readonly Lazy<Dictionary<string, string>> Resources = new(() =>
        Asm.GetManifestResourceNames()
            .Where(n => n.StartsWith("wwwroot", StringComparison.Ordinal))
            .ToDictionary(n => n["wwwroot".Length..].Replace('\\', '/').TrimStart('/'), n => n, StringComparer.Ordinal));

    private static async Task ServeStaticAsync(HttpListenerContext ctx, string rawPath)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        bool head = req.HttpMethod == "HEAD";
        if (req.HttpMethod != "GET" && !head) { res.StatusCode = 405; res.Close(); return; }
        string path = Uri.UnescapeDataString(rawPath).TrimStart('/');
        if (path.Length == 0) path = "index.html";
        if (path.Contains("..")) { res.StatusCode = 400; res.Close(); return; }

        if (!Resources.Value.TryGetValue(path, out var name))
        {
            if (path == "index.html") { await NotBuiltAsync(res); return; }
            if (!path.Contains('.') && Resources.Value.TryGetValue("index.html", out var index)) name = index;
            else { res.StatusCode = 404; res.Close(); return; }
            path = "index.html";
        }

        string ext = Path.GetExtension(path);
        res.ContentType = Types.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";
        res.Headers["Cache-Control"] = path.StartsWith("assets/", StringComparison.Ordinal)
            ? "public, max-age=31536000, immutable" : "no-cache";
        res.Headers["X-Content-Type-Options"] = "nosniff";
        if (path == "index.html") res.Headers["Referrer-Policy"] = "no-referrer";

        await using var s = Asm.GetManifestResourceStream(name)!;
        res.ContentLength64 = s.Length;
        if (!head) await s.CopyToAsync(res.OutputStream);
        res.Close();
    }

    private static async Task NotBuiltAsync(HttpListenerResponse res)
    {
        res.ContentType = "text/html; charset=utf-8";
        var bytes = System.Text.Encoding.UTF8.GetBytes("<!doctype html><meta name=viewport content='width=device-width'>" +
            "<body style='background:#0B0A09;color:#E9E4D8;font:15px system-ui;padding:28px'>" +
            "<h2>Nota Remote</h2><p>This build of Nota was made without the phone app (Node.js wasn't available). " +
            "Build src/web/remote and rebuild Nota.</p>");
        res.ContentLength64 = bytes.Length;
        await res.OutputStream.WriteAsync(bytes);
        res.Close();
    }
}
