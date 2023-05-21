using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace LittleForker;

public class ProcessExitedHelperTests
{
    private readonly ITestOutputHelper _outputHelper;
    private readonly ILoggerFactory    _loggerFactory;

    public ProcessExitedHelperTests(ITestOutputHelper outputHelper)
    {
        _outputHelper  = outputHelper;
        _loggerFactory = new XunitLoggerFactory(outputHelper).LoggerFactory;
    }

    [Fact]
    public async Task When_parent_process_does_not_exist_then_should_call_parent_exited_callback()
    {
        var parentExited = new TaskCompletionSource<int?>();
        using (new ProcessExitedHelper(-1, watcher => parentExited.SetResult(watcher.ProcessId), _loggerFactory))
        {
            var processId = await parentExited.Task.TimeoutAfter(TimeSpan.FromSeconds(2));
            processId.ShouldBe(-1);
        }
    }

    [Fact]
    public async Task When_parent_process_exits_than_should_call_parent_exited_callback()
    {
        // Start parent
        var settings =
            new ProcessSupervisorSettings(ProcessRunType.NonTerminating, Environment.CurrentDirectory, "dotnet")
            {
                Arguments = "./NonTerminatingProcess/NonTerminatingProcess.dll"
            };
        var supervisor      = new ProcessSupervisor(settings, _loggerFactory);
        var parentIsRunning = supervisor.WhenStateIs(ProcessSupervisor.State.Running);
        supervisor.OutputDataReceived += data => _outputHelper.WriteLine($"Parent Process: {data}");
        await supervisor.Start();
        await parentIsRunning;

        // Monitor parent
        var parentExited = new TaskCompletionSource<int?>();
        using (new ProcessExitedHelper(supervisor.ProcessInfo.Id, watcher => parentExited.SetResult(watcher.ProcessId), _loggerFactory))
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
        var parentSettings =
            new ProcessSupervisorSettings(ProcessRunType.NonTerminating, Environment.CurrentDirectory, "dotnet")
            {
                Arguments = "./NonTerminatingProcess/NonTerminatingProcess.dll"
            };

        var parentSupervisor = new ProcessSupervisor(parentSettings, _loggerFactory);
        parentSupervisor.OutputDataReceived += data => _outputHelper.WriteLine($"Parent: {data}");
        var parentIsRunning = parentSupervisor.WhenStateIs(ProcessSupervisor.State.Running);
        await parentSupervisor.Start();
        await parentIsRunning;

        // Start child
        var childSettings = new ProcessSupervisorSettings(ProcessRunType.SelfTerminating,  Environment.CurrentDirectory, "dotnet")
        {
            Arguments = $"./NonTerminatingProcess/NonTerminatingProcess.dll --ParentProcessId={parentSupervisor.ProcessInfo.Id}"
        };
        var childSupervisor = new ProcessSupervisor(childSettings, _loggerFactory);
        childSupervisor.OutputDataReceived += data => _outputHelper.WriteLine($"Child: {data}");
        var childIsRunning  = childSupervisor.WhenStateIs(ProcessSupervisor.State.Running);
        var childHasStopped = childSupervisor.WhenStateIs(ProcessSupervisor.State.ExitedSuccessfully);
        await childSupervisor.Start();
        await childIsRunning;

        // Stop parent
        await parentSupervisor.Stop();

        // Wait for child to stop
        await childHasStopped.TimeoutAfter(TimeSpan.FromSeconds(2));
    }
}