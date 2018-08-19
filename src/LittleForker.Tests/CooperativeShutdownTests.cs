using System;
using System.Diagnostics;
using System.Threading.Tasks;
using LittleForker.Infra;
using LittleForker.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace LittleForker
{
    public class CooperativeShutdownTests : IDisposable
    {
        private readonly IDisposable _logCapture;

        public CooperativeShutdownTests(ITestOutputHelper outputHelper)
        {
            _logCapture = LogHelper.Capture(outputHelper, LogProvider.SetCurrentLogProvider);
        }

        [Fact]
        public async Task When_server_signals_exit_then_should_notifiy_client_to_exit()
        {
            var exitCalled = new TaskCompletionSource<bool>();
            var listener = await CooperativeShutdown.Listen(
                () => exitCalled.SetResult(true));

            await CooperativeShutdown.SignalExit(Process.GetCurrentProcess().Id);

            (await exitCalled.Task.TimeoutAfter(TimeSpan.FromSeconds(2))).ShouldBeTrue();

            listener.Dispose();
        }

        public void Dispose()
        {
            _logCapture.Dispose();
        }
    }
}
