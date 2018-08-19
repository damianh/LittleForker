using System;
using System.Threading.Tasks;
using LittleForker.Infra;
using LittleForker.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace LittleForker
{
    public class MonitorParentProcessTests : IDisposable
    {
        private readonly ITestOutputHelper _outputHelper;
        private readonly IDisposable _logCapture;

        public MonitorParentProcessTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
            _logCapture = LogHelper.Capture(outputHelper, LogProvider.SetCurrentLogProvider);
            Environment.SetEnvironmentVariable(Constants.ProcessIdEnvironmentVariable, null);
        }

        [Fact]
        public async Task When_parent_process_does_not_exist_then_should_call_parent_exited_callback()
        {
            var parentExited = new TaskCompletionSource<int?>();
            var monitorParentProcess = new ProcessMonitor(
                pid => parentExited.SetResult(pid),
                -1);

            var processId = await parentExited.Task.TimeoutAfter(TimeSpan.FromSeconds(2));
            processId.ShouldBe(-1);
        }

        [Fact]
        public async Task Should_fall_back_to_enviroment_variable()
        {
            var parentExited = new TaskCompletionSource<int?>();
            Environment.SetEnvironmentVariable(Constants.ProcessIdEnvironmentVariable, "-101");
            var monitorParentProcess = new ProcessMonitor(
                pid => parentExited.SetResult(pid));

            var processId = await parentExited.Task.TimeoutAfter(TimeSpan.FromSeconds(2));
            processId.ShouldBe(-101);
        }

        [Fact]
        public async Task When_parent_process_exits_than_should_call_parent_exited_callback()
        {
            // Start parent
            var supervisor = new ProcessSupervisor(
                ProcessRunType.NonTerminating,
                Environment.CurrentDirectory,
                "dotnet",
                "./NonTerminatingProcess/NonTerminatingProcess.dll");
            var parentIsRunning = supervisor.WhenStateIs(ProcessSupervisor.State.Running);
            supervisor.OutputDataReceived += data => _outputHelper.WriteLine($"Parent Process: {data}");
            supervisor.Start();
            await parentIsRunning;

            // Monitor parent
            var parentExited = new TaskCompletionSource<int?>();
            using (new ProcessMonitor(
                pid => parentExited.SetResult(pid),
                supervisor.ProcessInfo.Id))
            {
                // Stop parent
                await supervisor.Stop(TimeSpan.FromSeconds(2));
                var processId = await parentExited.Task.TimeoutAfter(TimeSpan.FromSeconds(2));
                processId.Value.ShouldBeGreaterThan(0);
            }
        }

        [Fact]
        public async Task When_parent_process_exits_then_child_process_should_also_do_so()
        {
            // Start parent
            var parentSupervisor = new ProcessSupervisor(
                ProcessRunType.NonTerminating,
                Environment.CurrentDirectory,
                "dotnet",
                "./NonTerminatingProcess/NonTerminatingProcess.dll");
            parentSupervisor.OutputDataReceived += data => _outputHelper.WriteLine($"Parent: {data}");
            var parentIsRunning = parentSupervisor.WhenStateIs(ProcessSupervisor.State.Running);
            parentSupervisor.Start();
            await parentIsRunning;

            // Start child
            var childSupervisor = new ProcessSupervisor(
                ProcessRunType.SelfTerminating,
                Environment.CurrentDirectory,
                "dotnet",
                "./NonTerminatingProcess/NonTerminatingProcess.dll",
                parentProcessId: parentSupervisor.ProcessInfo.Id);
            childSupervisor.OutputDataReceived += data => _outputHelper.WriteLine($"Child: {data}");
            var childIsRunning = childSupervisor.WhenStateIs(ProcessSupervisor.State.Running);
            var childHasStopped = childSupervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
            childSupervisor.Start();
            await childIsRunning;

            // Stop parent
            await parentSupervisor.Stop(TimeSpan.FromSeconds(2));

            // Wait for child to stop
            await childHasStopped.TimeoutAfter(TimeSpan.FromSeconds(2));
        }

        public void Dispose()
        {
            _logCapture?.Dispose();
        }
    }
}