using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace LittleForker;

public sealed class ProcessSupervisorTests
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => b.AddConsole());

    [Fact]
    public async Task Given_invalid_process_path_then_state_should_be_StartError()
    {
        var supervisor = new ProcessSupervisor(_loggerFactory, ProcessRunType.NonTerminating, "c:/", "invalid.exe");
        var stateIsStartFailed = supervisor.WhenStateIs(ProcessSupervisor.State.StartFailed);
        await supervisor.Start();

        await stateIsStartFailed;
        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
        supervisor.OnStartException.ShouldNotBeNull();

        Console.WriteLine(supervisor.OnStartException.ToString());
    }

    [Fact]
    public async Task Given_invalid_working_directory_then_state_should_be_StartError()
    {
        var supervisor = new ProcessSupervisor(_loggerFactory, ProcessRunType.NonTerminating, "c:/does_not_exist", "git.exe");
        await supervisor.Start();

        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
        supervisor.OnStartException.ShouldNotBeNull();

        Console.WriteLine(supervisor.OnStartException.ToString());
    }

    [Fact]
    public async Task Given_short_running_exe_then_should_run_to_exit()
    {
        var envVars = new StringDictionary {{"a", "b"}};
        var supervisor = new ProcessSupervisor(
            _loggerFactory,
            ProcessRunType.SelfTerminating,
            Environment.CurrentDirectory,
            "dotnet",
            "./SelfTerminatingProcess/SelfTerminatingProcess.dll",
            envVars);
        supervisor.OutputDataReceived += data => Console.WriteLine(data);
        var whenStateIsExited          = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
        var whenStateIsExitedWithError = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedWithError);

        await supervisor.Start();

        var task = await Task.WhenAny(whenStateIsExited, whenStateIsExitedWithError);

        task.ShouldBe(whenStateIsExited);
        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.ExitedSuccessfully);
        supervisor.OnStartException.ShouldBeNull();
        supervisor.ProcessInfo.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Given_non_terminating_process_then_should_exit_when_stopped()
    {
        var supervisor = new ProcessSupervisor(
            _loggerFactory,
            ProcessRunType.NonTerminating,
            Environment.CurrentDirectory,
            "dotnet",
            "./NonTerminatingProcess/NonTerminatingProcess.dll");
        supervisor.OutputDataReceived += data => Console.WriteLine($"Process: {data}");
        var running = supervisor.WhenStateIs(ProcessSupervisor.State.Running);
        await supervisor.Start();

        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.Running);
        await running;

        await supervisor.Stop(TimeSpan.FromSeconds(5));

        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.ExitedSuccessfully);
        supervisor.OnStartException.ShouldBeNull();
        supervisor.ProcessInfo.ExitCode.ShouldBe(0);
    }
        
    [Fact]
    public async Task Can_restart_a_stopped_short_running_process()
    {
        var supervisor = new ProcessSupervisor(
            _loggerFactory,
            ProcessRunType.SelfTerminating,
            Environment.CurrentDirectory,
            "dotnet",
            "./SelfTerminatingProcess/SelfTerminatingProcess.dll");
        supervisor.OutputDataReceived += data => Console.WriteLine(data);
        var stateIsStopped = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
        await supervisor.Start();
        await stateIsStopped;

        await supervisor.Start();
        await stateIsStopped;
    }

    [Fact]
    public async Task Can_restart_a_stopped_long_running_process()
    {
        var supervisor = new ProcessSupervisor(
            _loggerFactory,
            ProcessRunType.NonTerminating,
            Environment.CurrentDirectory,
            "dotnet",
            "./NonTerminatingProcess/NonTerminatingProcess.dll");
        supervisor.OutputDataReceived += data => Console.WriteLine(data);
        var exitedKilled = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedKilled);
        await supervisor.Start();
        await supervisor.Stop();
        await exitedKilled.TimeoutAfter(TimeSpan.FromSeconds(5));

        // Restart
        var exitedSuccessfully = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
        await supervisor.Start();
        await supervisor.Stop(TimeSpan.FromSeconds(2));
        await exitedSuccessfully;
    }

    [Fact]
    public async Task When_stop_a_non_terminating_process_without_a_timeout_then_should_exit_killed()
    {
        var supervisor = new ProcessSupervisor(
            _loggerFactory,
            ProcessRunType.NonTerminating,
            Environment.CurrentDirectory,
            "dotnet",
            "./NonTerminatingProcess/NonTerminatingProcess.dll");
        supervisor.OutputDataReceived += data => Console.WriteLine(data);
        var stateIsStopped = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedKilled);
        await supervisor.Start();
        await supervisor.Stop(); // No timeout so will just kill the process
        await stateIsStopped.TimeoutAfter(TimeSpan.FromSeconds(2));

        Console.WriteLine($"Exit code {supervisor.ProcessInfo.ExitCode}");
    }
        
    [Fact]
    public async Task When_stop_a_non_terminating_process_that_does_not_shutdown_within_timeout_then_should_exit_killed()
    {
        var supervisor = new ProcessSupervisor(
            _loggerFactory,
            ProcessRunType.NonTerminating,
            Environment.CurrentDirectory,
            "dotnet",
            "./NonTerminatingProcess/NonTerminatingProcess.dll --ignore-shutdown-signal=true");
        supervisor.OutputDataReceived += data => Console.WriteLine(data);
        var stateIsKilled = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedKilled);
        await supervisor.Start();
        await supervisor.Stop(TimeSpan.FromSeconds(2));
        await stateIsKilled.TimeoutAfter(TimeSpan.FromSeconds(5));

        Console.WriteLine($"Exit code {supervisor.ProcessInfo.ExitCode}");
    }

    [Fact]
    public async Task When_stop_a_non_terminating_process_with_non_zero_then_should_exit_error()
    {
        var supervisor = new ProcessSupervisor(
            _loggerFactory,
            ProcessRunType.NonTerminating,
            Environment.CurrentDirectory,
            "dotnet",
            "./NonTerminatingProcess/NonTerminatingProcess.dll --exit-with-non-zero=true");
        supervisor.OutputDataReceived += data => Console.WriteLine(data);
        var stateExitWithError = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedWithError);
        await supervisor.Start();
        await supervisor.Stop(TimeSpan.FromSeconds(5));
        await stateExitWithError.TimeoutAfter(TimeSpan.FromSeconds(5));
        supervisor.ProcessInfo.ExitCode.ShouldNotBe(0);

        Console.WriteLine($"Exit code {supervisor.ProcessInfo.ExitCode}");
    }

    [Fact]
    public async Task Can_attempt_to_restart_a_failed_short_running_process()
    {
        var supervisor = new ProcessSupervisor(
            _loggerFactory,
            ProcessRunType.NonTerminating,
            Environment.CurrentDirectory,
            "invalid.exe");
        await supervisor.Start();

        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
        supervisor.OnStartException.ShouldNotBeNull();

        await supervisor.Start();

        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
        supervisor.OnStartException.ShouldNotBeNull();
    }

    [Fact]
    public async Task Can_restart_a_process_that_exited_with_error()
    {
        var supervisor = new ProcessSupervisor(
            _loggerFactory,
            ProcessRunType.NonTerminating,
            Environment.CurrentDirectory,
            "dotnet",
            "./NonTerminatingProcess/NonTerminatingProcess.dll --exit-with-non-zero=true");
        supervisor.OutputDataReceived += data => Console.WriteLine($"Process: {data}");

        // First run — stop cooperatively; process exits with non-zero code.
        var exitedWithError = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedWithError);
        await supervisor.Start();
        await supervisor.Stop(TimeSpan.FromSeconds(5));
        await exitedWithError.TimeoutAfter(TimeSpan.FromSeconds(5));
        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.ExitedWithError);
        supervisor.ProcessInfo.ExitCode.ShouldNotBe(0);

        // Restart from ExitedWithError — should transition back to Running and exit again.
        var exitedWithError2 = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedWithError);
        await supervisor.Start();
        await supervisor.Stop(TimeSpan.FromSeconds(5));
        await exitedWithError2.TimeoutAfter(TimeSpan.FromSeconds(5));
        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.ExitedWithError);
    }

    [Fact]
    public async Task When_stop_a_self_terminating_process_then_should_exit()
    {
        var supervisor = new ProcessSupervisor(
            _loggerFactory,
            ProcessRunType.SelfTerminating,
            Environment.CurrentDirectory,
            "dotnet",
            "./NonTerminatingProcess/NonTerminatingProcess.dll");
        supervisor.OutputDataReceived += data => Console.WriteLine($"Process: {data}");

        var exitedSuccessfully = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
        var exitedKilled = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedKilled);
        await supervisor.Start();
        await supervisor.Stop(TimeSpan.FromSeconds(5));

        // Should reach an exit state without an InvalidOperationException from Stateless.
        var completed = await Task.WhenAny(exitedSuccessfully, exitedKilled).TimeoutAfter(TimeSpan.FromSeconds(10));
        var finalState = supervisor.CurrentState;
        (finalState == ProcessSupervisor.State.ExitedSuccessfully
            || finalState == ProcessSupervisor.State.ExitedKilled)
            .ShouldBeTrue($"Expected ExitedSuccessfully or ExitedKilled, got {finalState}");
    }

    [Fact]
    public async Task WhenStateIs_already_in_state_completes_immediately()
    {
        // Initial state is NotStarted — WhenStateIs(NotStarted) should complete immediately.
        var supervisor = new ProcessSupervisor(
            _loggerFactory,
            ProcessRunType.SelfTerminating,
            Environment.CurrentDirectory,
            "dotnet",
            "./SelfTerminatingProcess/SelfTerminatingProcess.dll");

        var task = supervisor.WhenStateIs(ProcessSupervisor.State.NotStarted);
        task.IsCompleted.ShouldBeTrue("WhenStateIs should complete immediately when already in target state");

        // Also verify after a state transition.
        var exited = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
        await supervisor.Start();
        await exited.TimeoutAfter(TimeSpan.FromSeconds(5));

        var task2 = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
        task2.IsCompleted.ShouldBeTrue("WhenStateIs should complete immediately for ExitedSuccessfully");
    }

    [Fact]
    public void WriteDotGraph()
    {
        var processController = new ProcessSupervisor(
            _loggerFactory, 
            ProcessRunType.NonTerminating, 
            Environment.CurrentDirectory,
            "invalid.exe");
        Console.WriteLine(processController.GetDotGraph());
    }
}
