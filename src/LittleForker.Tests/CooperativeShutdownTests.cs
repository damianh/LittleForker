// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    [Fact]
    public async Task When_server_signals_exit_via_hosted_service_then_should_stop_application()
    {
        var applicationLifetime = new FakeHostApplicationLifetime();
        var options = new CooperativeShutdownHostedServiceOptions
        {
            PipeName = Guid.NewGuid().ToString()
        };
        var service = new CooperativeShutdownHostedService(
            applicationLifetime,
            Options.Create(options),
            _loggerFactory.CreateLogger<CooperativeShutdownHostedService>());

        await service.StartAsync(CancellationToken.None);

        await CooperativeShutdown.SignalExit(options.PipeName!, _loggerFactory);

        await applicationLifetime.StopApplicationCalled.TimeoutAfter(TimeSpan.FromSeconds(5));

        await service.StopAsync(CancellationToken.None);
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly TaskCompletionSource _stopApplicationCalled = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => _stopApplicationCalled.TrySetResult();

        public Task StopApplicationCalled => _stopApplicationCalled.Task;
    }
}
