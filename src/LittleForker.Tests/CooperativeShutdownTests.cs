using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace LittleForker;

public class CooperativeShutdownTests
{
    private readonly ILoggerFactory _loggerFactory;

    public CooperativeShutdownTests(ITestOutputHelper outputHelper)
    {
        _loggerFactory = new XunitLoggerFactory(outputHelper).LoggerFactory;
    }

    [Fact]
    public async Task When_server_signals_exit_then_should_notify_client_to_exit()
    {
        var applicationLifetime             = new FakeHostApplicationLifetime();
        var options = new CooperativeShutdownHostedServiceOptions()
        {
            PipeName = Guid.NewGuid().ToString()
        };
        var cooperativeShutdownHostedService = new CooperativeShutdownHostedService(
            applicationLifetime,
            Options.Create(options),
            _loggerFactory.CreateLogger<CooperativeShutdownHostedService>());

        await cooperativeShutdownHostedService.StartAsync(CancellationToken.None);

        await CooperativeShutdown.SignalExit(options.PipeName, _loggerFactory);

        await applicationLifetime.StopApplicationCalled.TimeoutAfter(TimeSpan.FromSeconds(5));

        await cooperativeShutdownHostedService.StopAsync(CancellationToken.None);
    }


    private class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly TaskCompletionSource _stopApplicationCalled = new();
        public           CancellationToken    ApplicationStarted  => throw new NotImplementedException();
        public           CancellationToken    ApplicationStopping => throw new NotImplementedException();
        public           CancellationToken    ApplicationStopped  => throw new NotImplementedException();

        public void StopApplication()
        {
            _stopApplicationCalled.SetResult();
        }

        public Task StopApplicationCalled => _stopApplicationCalled.Task;
    }
}