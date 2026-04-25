// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace LittleForker;

public sealed class CooperativeShutdownTests
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => b.AddConsole());

    [Fact]
    public async Task When_server_signals_exit_then_should_notify_client_to_exit()
    {
        var exitCalled = new TaskCompletionSource<bool>();
        var listener = await CooperativeShutdown.Listen(
            () => exitCalled.SetResult(true),
            _loggerFactory);

        await CooperativeShutdown.SignalExit(Process.GetCurrentProcess().Id, _loggerFactory);

        (await exitCalled.Task).ShouldBeTrue();

        listener.Dispose();
    }
}
