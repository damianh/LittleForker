using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using LittleForker.Infra;
using LittleForker.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace LittleForker
{
    public class ProcessSupervisorTests : IDisposable
    {
        private readonly ITestOutputHelper _outputHelper;
        private readonly IDisposable _logCapture;

        public ProcessSupervisorTests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
            _logCapture = LogHelper.Capture(outputHelper, LogProvider.SetCurrentLogProvider);
        }

        [Fact]
        public async Task Given_invalid_process_path_then_state_should_be_StartError()
        {
            var supervisor = new ProcessSupervisor(ProcessRunType.NonTerminating, "c:/", "invalid.exe");
            var stateIsStartFailed = supervisor.WhenStateIs(ProcessSupervisor.State.StartFailed);
            supervisor.Start();

            await stateIsStartFailed;
            supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
            supervisor.OnStartException.ShouldNotBeNull();

            _outputHelper.WriteLine(supervisor.OnStartException.ToString());
        }

        [Fact]
        public void Given_invalid_working_directory_then_state_should_be_StartError()
        {
            var supervisor = new ProcessSupervisor(ProcessRunType.NonTerminating, "c:/does_not_exist", "git.exe");
            supervisor.Start();

            supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
            supervisor.OnStartException.ShouldNotBeNull();

            _outputHelper.WriteLine(supervisor.OnStartException.ToString());
        }

        [Fact]
        public async Task Given_short_running_exe_then_should_run_to_exit()
        {
            var envVars = new StringDictionary {{"a", "b"}};
            var supervisor = new ProcessSupervisor(
                ProcessRunType.SelfTerminating,
                Environment.CurrentDirectory,
                "dotnet",
                "./SelfTerminatingProcess/SelfTerminatingProcess.dll",
                envVars);
            supervisor.OutputDataReceived += data => _outputHelper.WriteLine2(data);
            var whenStateIsExited = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
            var whenStateIsExitedWithError = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedWithError);

            supervisor.Start();

            var task = await Task.WhenAny(whenStateIsExited, whenStateIsExitedWithError);

            task.ShouldBe(whenStateIsExited);
            supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.OnStartException.ShouldBeNull();
            supervisor.ProcessInfo.ExitCode.ShouldBe(0);
        }

        [Fact]
        public async Task Given_long_running_exe_then_should_exit_when_stopped()
        {
            var supervisor = new ProcessSupervisor(
                ProcessRunType.NonTerminating,
                Environment.CurrentDirectory,
                "dotnet",
                "./NonTerminatingProcess/NonTerminatingProcess.dll");
            supervisor.OutputDataReceived += data => _outputHelper.WriteLine2($"Process: {data}");
            var running = supervisor.WhenStateIs(ProcessSupervisor.State.Running);
            var exitedSuccessfully = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.Start();
            await running;

            await supervisor.Stop(TimeSpan.FromSeconds(4));
            await exitedSuccessfully;

            supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.OnStartException.ShouldBeNull();
            supervisor.ProcessInfo.ExitCode.ShouldBe(0);
        }
        
        [Fact]
        public async Task Can_restart_a_stopped_short_running_process()
        {
            var supervisor = new ProcessSupervisor(
                ProcessRunType.SelfTerminating,
                Environment.CurrentDirectory,
                "dotnet",
                "./SelfTerminatingProcess/SelfTerminatingProcess.dll");
            supervisor.OutputDataReceived += data => _outputHelper.WriteLine2(data);
            var stateIsStopped = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.Start();
            await stateIsStopped;

            supervisor.Start();
            await stateIsStopped;
        }

        [Fact]
        public async Task Can_restart_a_stopped_long_running_process()
        {
            var supervisor = new ProcessSupervisor(ProcessRunType.NonTerminating, Environment.CurrentDirectory, "dotnet", "./NonTerminatingProcess/NonTerminatingProcess.dll");
            supervisor.OutputDataReceived += data => _outputHelper.WriteLine2(data);
            var stateIsStopped = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.Start();
            await supervisor.Stop(TimeSpan.FromSeconds(2));
            await stateIsStopped.TimeoutAfter(TimeSpan.FromSeconds(2));

            // Restart
            stateIsStopped = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.Start();
            await supervisor.Stop(TimeSpan.FromSeconds(2));
            await stateIsStopped;
        }

        [Fact]
        public async Task When_stop_a_non_terminating_process_then_should_exit_successfully()
        {
            var supervisor = new ProcessSupervisor(ProcessRunType.NonTerminating, Environment.CurrentDirectory, "dotnet", "./NonTerminatingProcess/NonTerminatingProcess.dll");
            supervisor.OutputDataReceived += data => _outputHelper.WriteLine2(data);
            var stateIsStopped = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
            supervisor.Start();
            await supervisor.Stop(); // No timeout so will just kill the process
            await stateIsStopped.TimeoutAfter(TimeSpan.FromSeconds(2));

            _outputHelper.WriteLine($"Exit code {supervisor.ProcessInfo.ExitCode}");
        }

        [Fact]
        public void Can_attempt_to_restart_a_failed_short_running_process()
        {
            var supervisor = new ProcessSupervisor(ProcessRunType.NonTerminating, Environment.CurrentDirectory, "invalid.exe");
            supervisor.Start();

            supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
            supervisor.OnStartException.ShouldNotBeNull();

            supervisor.Start();

            supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
            supervisor.OnStartException.ShouldNotBeNull();
        }

        [Fact]
        public void WriteDotGraph()
        {
            var processController = new ProcessSupervisor(ProcessRunType.NonTerminating, Environment.CurrentDirectory, "invalid.exe");
            _outputHelper.WriteLine(processController.GetDotGraph());
        }

        public void Dispose()
        {
            _logCapture?.Dispose();
        }
    }
}