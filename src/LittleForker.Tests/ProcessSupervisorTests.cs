using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace LittleForker;

public class ProcessSupervisorTests
{
    private readonly ITestOutputHelper _outputHelper;
    private readonly ILoggerFactory    _loggerFactory;

    public ProcessSupervisorTests(ITestOutputHelper outputHelper)
    {
        _outputHelper  = outputHelper;
        _loggerFactory = new XunitLoggerFactory(outputHelper).LoggerFactory;
    }

    [Fact]
    public async Task Given_invalid_process_path_then_state_should_be_StartError()
    {
        var settings = new ProcessSupervisorSettings("c:/", "invalid.exe");
        var supervisor = new ProcessSupervisor(settings, _loggerFactory);
        var stateIsStartFailed = supervisor.WhenStateIs(ProcessSupervisor.State.StartFailed);
        await supervisor.Start();

        await stateIsStartFailed;
        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
        supervisor.OnStartException.ShouldNotBeNull();

        _outputHelper.WriteLine(supervisor.OnStartException.ToString());
    }

    [Fact]
    public async Task Given_invalid_working_directory_then_state_should_be_StartError()
    {
        var settings = new ProcessSupervisorSettings("c:/does_not_exist", "git.exe");
        var supervisor = new ProcessSupervisor(settings, _loggerFactory);
        await supervisor.Start();

        supervisor.CurrentState.ShouldBe(ProcessSupervisor.State.StartFailed);
        supervisor.OnStartException.ShouldNotBeNull();

        _outputHelper.WriteLine(supervisor.OnStartException.ToString());
    }

    [Fact]
    public async Task Given_short_running_exe_then_should_run_to_exit()
    {
        var envVars = new StringDictionary {{"a", "b"}};
        var settings = new ProcessSupervisorSettings(Environment.CurrentDirectory, "dotnet")
        {
            Arguments = "./SelfTerminatingProcess/SelfTerminatingProcess.dll",
            EnvironmentVariables = envVars
        };
        var supervisor = new ProcessSupervisor(settings, _loggerFactory);
        supervisor.OutputDataReceived += data => _outputHelper.WriteLine2(data);
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
        var settings = new ProcessSupervisorSettings(Environment.CurrentDirectory, "dotnet")
        {
            Arguments = "./NonTerminatingProcess/NonTerminatingProcess.dll"
        };
        var supervisor = new ProcessSupervisor(settings, _loggerFactory);
        supervisor.OutputDataReceived += data => _outputHelper.WriteLine2($"Process: {data}");
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
    public async Task Can_restart_a_stopped_long_running_process()
    {
        var settings = new ProcessSupervisorSettings(Environment.CurrentDirectory, "dotnet")
        {
            Arguments = "./NonTerminatingProcess/NonTerminatingProcess.dll"
        };
        var supervisor = new ProcessSupervisor(settings, _loggerFactory);
        supervisor.OutputDataReceived += data => _outputHelper.WriteLine2(data);
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
        supervisor.OutputDataReceived += data => _outputHelper.WriteLine2(data);
        var stateIsStopped = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedKilled);
        await supervisor.Start();
        await supervisor.Stop(); // No timeout so will just kill the process
        await stateIsStopped.TimeoutAfter(TimeSpan.FromSeconds(2));

        _outputHelper.WriteLine($"Exit code {supervisor.ProcessInfo.ExitCode}");
    }
        
    [Fact]
    public async Task When_stop_a_non_terminating_process_that_does_not_shutdown_within_timeout_then_should_exit_killed()
    {
        var settings = new ProcessSupervisorSettings(Environment.CurrentDirectory, "dotnet")
        {
            Arguments = "./NonTerminatingProcess/NonTerminatingProcess.dll --ignore-shutdown-signal=true"
        };
        var supervisor = new ProcessSupervisor(settings, _loggerFactory);
        supervisor.OutputDataReceived += data => _outputHelper.WriteLine2(data);
        var stateIsKilled = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedKilled);
        await supervisor.Start();
        await supervisor.Stop(TimeSpan.FromSeconds(2));
        await stateIsKilled.TimeoutAfter(TimeSpan.FromSeconds(5));

        _outputHelper.WriteLine($"Exit code {supervisor.ProcessInfo.ExitCode}");
    }

    [Fact]
    public async Task When_stop_a_non_terminating_process_with_non_zero_then_should_exit_error()
    {
        var settings = new ProcessSupervisorSettings(Environment.CurrentDirectory, "dotnet")
        {
            Arguments = "./NonTerminatingProcess/NonTerminatingProcess.dll --exit-with-non-zero=true"
        };
        var supervisor = new ProcessSupervisor(settings, _loggerFactory);
        supervisor.OutputDataReceived += data => _outputHelper.WriteLine2(data);
        var stateExitWithError = supervisor.WhenStateIs(ProcessSupervisor.State.ExitedWithError);
        await supervisor.Start();
        await supervisor.Stop(TimeSpan.FromSeconds(5));
        await stateExitWithError.TimeoutAfter(TimeSpan.FromSeconds(5));
        supervisor.ProcessInfo.ExitCode.ShouldNotBe(0);

        _outputHelper.WriteLine($"Exit code {supervisor.ProcessInfo.ExitCode}");
    }

    [Fact]
    public void WriteDotGraph()
    {
        var settings = new ProcessSupervisorSettings(Environment.CurrentDirectory,
            "invalid.exe");
        var processController = new ProcessSupervisor(settings, _loggerFactory);
        _outputHelper.WriteLine(processController.GetDotGraph());
    }
}