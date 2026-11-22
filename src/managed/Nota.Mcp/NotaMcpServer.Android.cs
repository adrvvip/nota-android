// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Android build: ASP.NET Core (Kestrel) does not exist for Android, so the MCP server is not
// offered there. Same surface as NotaMcpServer.cs; starting it reports why it can't.

using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.Mcp;

public sealed class NotaMcpServer : IAsyncDisposable
{
    public bool Running => false;
    public string? Url => null;

    public Task StartAsync(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh, int port,
        Action<IServiceCollection>? configureServices = null)
        => throw new PlatformNotSupportedException("The MCP server is not available on Android.");

    public Task StopAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
