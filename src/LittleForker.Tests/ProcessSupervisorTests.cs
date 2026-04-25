// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

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
        var supervisor = new ProcessSupervisor(new ProcessSupervisorSettings("c:/", "invalid.exe"), _loggerFactory);
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
        var supervisor = new ProcessSupervisor(new ProcessSupervisorSettings("c:/does_not_exist", "git.exe"), _loggerFactory);
        await supervisor.Start();

        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
        supervisor.OnStartException.ShouldNotBeNull();

        Console.WriteLine(supervisor.OnStartException.ToString());
    }

    [Fact]
    public async Task Given_non_terminating_process_then_should_exit_when_stopped()
    {
        var settings = new ProcessSupervisorSettings(Environment.CurrentDirectory, "dotnet")
        {
            Arguments = "./NonTerminatingProcess/NonTerminatingProcess.dll"
        };
        var supervisor = new ProcessSupervisor(settings, _loggerFactory);
        supervisor.OutputDataReceived += data => Console.WriteLine($"Process: {data}");
        var running = supervisor.WhenStateIs(ProcessSupervisor.State.Running);
        await supervisor.Start();

        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.Running);
        await running;

        await supervisor.Stop(TimeSpan.FromSeconds(5));

        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.ExitedSuccessfully);
        supervisor.OnStartException.ShouldBeNull();
        supervisor.ProcessInfo!.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task Can_restart_a_stopped_long_running_process()
    {
        var settings = new ProcessSupervisorSettings(Environment.CurrentDirectory, "dotnet")
        {
            Arguments = "./NonTerminatingProcess/NonTerminatingProcess.dll"
        };
        var supervisor = new ProcessSupervisor(settings, _loggerFactory);
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
        var settings = new ProcessSupervisorSettings(Environment.CurrentDirectory, "dotnet")
        {
            Arguments = "./NonTerminatingProcess/NonTerminatingProcess.dll"
        };
        var supervisor = new ProcessSupervisor(settings, _loggerFactory);
        supervisor.OutputDataReceived += data => Console.WriteLine(data);
        var stateIsStopped = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedKilled);
        await supervisor.Start();
        await supervisor.Stop(); // No timeout so will just kill the process
        await stateIsStopped.TimeoutAfter(TimeSpan.FromSeconds(2));

        Console.WriteLine($"Exit code {supervisor.ProcessInfo!.ExitCode}");
    }

    [Fact]
    public async Task When_stop_a_non_terminating_process_that_does_not_shutdown_within_timeout_then_should_exit_killed()
    {
        var settings = new ProcessSupervisorSettings(Environment.CurrentDirectory, "dotnet")
        {
            Arguments = "./NonTerminatingProcess/NonTerminatingProcess.dll --ignore-shutdown-signal=true"
        };
        var supervisor = new ProcessSupervisor(settings, _loggerFactory);
        supervisor.OutputDataReceived += data => Console.WriteLine(data);
        var stateIsKilled = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedKilled);
        await supervisor.Start();
        await supervisor.Stop(TimeSpan.FromSeconds(2));
        await stateIsKilled.TimeoutAfter(TimeSpan.FromSeconds(5));

        Console.WriteLine($"Exit code {supervisor.ProcessInfo!.ExitCode}");
    }

    [Fact]
    public async Task When_stop_a_non_terminating_process_with_non_zero_then_should_exit_error()
    {
        var settings = new ProcessSupervisorSettings(Environment.CurrentDirectory, "dotnet")
        {
            Arguments = "./NonTerminatingProcess/NonTerminatingProcess.dll --exit-with-non-zero=true"
        };
        var supervisor = new ProcessSupervisor(settings, _loggerFactory);
        supervisor.OutputDataReceived += data => Console.WriteLine(data);
        var stateExitWithError = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedWithError);
        await supervisor.Start();
        await supervisor.Stop(TimeSpan.FromSeconds(5));
        await stateExitWithError.TimeoutAfter(TimeSpan.FromSeconds(5));
        supervisor.ProcessInfo!.ExitCode.ShouldNotBe(0);

        Console.WriteLine($"Exit code {supervisor.ProcessInfo.ExitCode}");
    }

    [Fact]
    public async Task Can_attempt_to_restart_a_failed_process()
    {
        var supervisor = new ProcessSupervisor(
            new ProcessSupervisorSettings(Environment.CurrentDirectory, "invalid.exe"),
            _loggerFactory);
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
        var settings = new ProcessSupervisorSettings(Environment.CurrentDirectory, "dotnet")
        {
            Arguments = "./NonTerminatingProcess/NonTerminatingProcess.dll --exit-with-non-zero=true"
        };
        var supervisor = new ProcessSupervisor(settings, _loggerFactory);
        supervisor.OutputDataReceived += data => Console.WriteLine($"Process: {data}");

        // First run — stop cooperatively; process exits with non-zero code.
        var exitedWithError = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedWithError);
        await supervisor.Start();
        await supervisor.Stop(TimeSpan.FromSeconds(5));
        await exitedWithError.TimeoutAfter(TimeSpan.FromSeconds(5));
        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.ExitedWithError);
        supervisor.ProcessInfo!.ExitCode.ShouldNotBe(0);

        // Restart from ExitedWithError — should transition back to Running and exit again.
        await supervisor.Start();
        var exitedWithError2 = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedWithError);
        await supervisor.Stop(TimeSpan.FromSeconds(5));
        await exitedWithError2.TimeoutAfter(TimeSpan.FromSeconds(5));
        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.ExitedWithError);
    }

    [Fact]
    public async Task WhenStateIs_already_in_state_completes_immediately()
    {
        // Initial state is NotStarted — WhenStateIs(NotStarted) should complete immediately.
        var supervisor = new ProcessSupervisor(
            new ProcessSupervisorSettings(Environment.CurrentDirectory, "dotnet")
            {
                Arguments = "./NonTerminatingProcess/NonTerminatingProcess.dll"
            },
            _loggerFactory);

        var task = supervisor.WhenStateIs(ProcessSupervisor.State.NotStarted);
        task.IsCompleted.ShouldBeTrue("WhenStateIs should complete immediately when already in target state");

        // Also verify after a state transition.
        var running = supervisor.WhenStateIs(ProcessSupervisor.State.Running);
        var exited = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
        await supervisor.Start();
        await running;
        await supervisor.Stop(TimeSpan.FromSeconds(5));
        await exited.TimeoutAfter(TimeSpan.FromSeconds(5));

        var task2 = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
        task2.IsCompleted.ShouldBeTrue("WhenStateIs should complete immediately for ExitedSuccessfully");
    }

    [Fact]
    public void WriteDotGraph()
    {
        var supervisor = new ProcessSupervisor(
            new ProcessSupervisorSettings(Environment.CurrentDirectory, "invalid.exe"),
            _loggerFactory);
        Console.WriteLine(supervisor.GetDotGraph());
    }
}
