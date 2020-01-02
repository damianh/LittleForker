using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace LittleForker
{
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
            var exitCalled = new TaskCompletionSource<bool>();
            var listener = await CooperativeShutdown.Listen(
                () => exitCalled.SetResult(true),
                _loggerFactory);

            await CooperativeShutdown.SignalExit(Process.GetCurrentProcess().Id, _loggerFactory);

            (await exitCalled.Task).ShouldBeTrue();

            listener.Dispose();
        }
    }
}
