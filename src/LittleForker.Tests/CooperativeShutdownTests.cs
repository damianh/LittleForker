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

    [Fact]
    public async Task When_server_signals_exit_with_nonce_then_should_notify_client()
    {
        var nonce = Guid.NewGuid().ToString();
        var exitCalled = new TaskCompletionSource<bool>();
        var listener = await CooperativeShutdown.Listen(
            () => exitCalled.SetResult(true),
            _loggerFactory,
            nonce);

        await CooperativeShutdown.SignalExit(Process.GetCurrentProcess().Id, _loggerFactory, nonce);

        (await exitCalled.Task).ShouldBeTrue();

        listener.Dispose();
    }

    [Fact]
    public async Task When_server_signals_exit_with_wrong_nonce_then_should_not_notify_client()
    {
        var nonce = Guid.NewGuid().ToString();
        var exitCalled = new TaskCompletionSource<bool>();
        var listener = await CooperativeShutdown.Listen(
            () => exitCalled.SetResult(true),
            _loggerFactory,
            nonce);

        // Signal with wrong nonce — should connect but not trigger shutdown.
        var result = await CooperativeShutdown.TrySignalExit(Process.GetCurrentProcess().Id, _loggerFactory, "wrong-nonce");

        // The signal should fail (timeout waiting for OK since listener rejects the command).
        result.ShouldBeFalse();

        listener.Dispose();
    }

    [Fact]
    public async Task When_hosted_service_has_nonce_and_correct_nonce_sent_then_should_stop()
    {
        var nonce = Guid.NewGuid().ToString();
        var applicationLifetime = new FakeHostApplicationLifetime();
        var options = new CooperativeShutdownHostedServiceOptions
        {
            PipeName = Guid.NewGuid().ToString(),
            Nonce = nonce
        };
        var service = new CooperativeShutdownHostedService(
            applicationLifetime,
            Options.Create(options),
            _loggerFactory.CreateLogger<CooperativeShutdownHostedService>());

        await service.StartAsync(CancellationToken.None);

        // Use TrySignalExitCore indirectly via the pipe name overload — send nonce via wire protocol.
        // We need to connect to the pipe and send "EXIT {nonce}" manually.
        using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", options.PipeName!, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        await pipe.ConnectAsync((int)TimeSpan.FromSeconds(3).TotalMilliseconds);
        var writer = new StreamWriter(pipe);
        var reader = new StreamReader(pipe, true);
        await writer.WriteLineAsync($"EXIT {nonce}");
        await writer.FlushAsync();
        var response = await reader.ReadLineAsync().TimeoutAfter(TimeSpan.FromSeconds(3));
        response.ShouldBe("OK");

        await applicationLifetime.StopApplicationCalled.TimeoutAfter(TimeSpan.FromSeconds(5));

        await service.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData("EXIT", null, true)]
    [InlineData("EXIT secret", null, true)]
    [InlineData("EXIT", "secret", false)]
    [InlineData("EXIT secret", "secret", true)]
    [InlineData("EXIT wrong", "secret", false)]
    [InlineData("QUIT", null, false)]
    [InlineData(null, null, false)]
    [InlineData(null, "secret", false)]
    [InlineData("EXIT ", null, true)]
    public void IsValidExitCommand_validates_correctly(string? command, string? nonce, bool expected)
        => CooperativeShutdown.IsValidExitCommand(command, nonce).ShouldBe(expected);

    [Fact]
    public void CreateSecurePipeServer_creates_pipe_with_single_instance()
    {
        var pipeName = Guid.NewGuid().ToString();
        using var pipe = CooperativeShutdown.CreateSecurePipeServer(pipeName);

        pipe.ShouldNotBeNull();

        // Attempting to create a second server on the same pipe should fail.
        Should.Throw<IOException>(() => CooperativeShutdown.CreateSecurePipeServer(pipeName));
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
