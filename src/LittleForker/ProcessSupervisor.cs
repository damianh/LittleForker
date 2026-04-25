// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Stateless;
using Stateless.Graph;

namespace LittleForker;

/// <summary>
///     Launches a process and tracks its lifecycle.
/// </summary>
public class ProcessSupervisor : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _arguments;
    private readonly System.Collections.Specialized.StringDictionary? _environmentVariables;
    private readonly bool _captureStdErr;
    private readonly string _processPath;
    private readonly StateMachine<State, Trigger>.TriggerWithParameters<Exception> _startErrorTrigger;
    private readonly StateMachine<State, Trigger>.TriggerWithParameters<TimeSpan?> _stopTrigger;
    private readonly StateMachine<State, Trigger> _processStateMachine = new(State.NotStarted, FiringMode.Immediate);
    private readonly string _workingDirectory;
    private Process? _process;
    private readonly ILoggerFactory _loggerFactory;
    private readonly InterlockedBoolean _killed = new();
    private readonly TaskQueue _taskQueue = new();

    /// <summary>
    ///     The state a process is in.
    /// </summary>
    public enum State
    {
        NotStarted,
        Running,
        StartFailed,
        Stopping,
        ExitedSuccessfully,
        ExitedWithError,
        ExitedKilled
    }

    private enum Trigger
    {
        Start,
        StartError,
        Stop,
        ProcessExit
    }

    /// <summary>
    ///     Initializes a new instance of <see cref="ProcessSupervisor"/>.
    /// </summary>
    /// <param name="settings">The settings used to launch and configure the process.</param>
    /// <param name="loggerFactory">A logger factory.</param>
    public ProcessSupervisor(ProcessSupervisorSettings settings, ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _workingDirectory = settings.WorkingDirectory;
        _processPath = settings.ProcessPath;
        _arguments = settings.Arguments ?? string.Empty;
        _environmentVariables = settings.EnvironmentVariables;
        _captureStdErr = settings.CaptureStdErr;

        _logger = loggerFactory.CreateLogger($"{nameof(LittleForker)}.{nameof(ProcessSupervisor)}-{settings.ProcessPath}");

        _processStateMachine
            .Configure(State.NotStarted)
            .Permit(Trigger.Start, State.Running);

        _startErrorTrigger = _processStateMachine.SetTriggerParameters<Exception>(Trigger.StartError);
        _stopTrigger = _processStateMachine.SetTriggerParameters<TimeSpan?>(Trigger.Stop);

        _processStateMachine
            .Configure(State.Running)
            .OnEntryFrom(Trigger.Start, OnStart)
            .PermitIf(
                Trigger.ProcessExit,
                State.ExitedSuccessfully,
                () => _process!.HasExited && _process!.ExitCode == 0,
                "ExitCode==0")
            .PermitIf(
                Trigger.ProcessExit,
                State.ExitedWithError,
                () => _process!.HasExited && _process!.ExitCode != 0,
                "ExitCode!=0")
            .Permit(Trigger.Stop, State.Stopping)
            .Permit(Trigger.StartError, State.StartFailed);

        _processStateMachine
            .Configure(State.StartFailed)
            .OnEntryFrom(_startErrorTrigger, OnStartError);

        _processStateMachine
            .Configure(State.Stopping)
            .OnEntryFromAsync(_stopTrigger, OnStop)
            .PermitIf(Trigger.ProcessExit, State.ExitedSuccessfully,
                () => !_killed.Value && _process!.HasExited && _process!.ExitCode == 0,
                "Shut down cleanly")
            .PermitIf(Trigger.ProcessExit, State.ExitedWithError,
                () => !_killed.Value && _process!.HasExited && _process!.ExitCode != 0,
                "Shut down with non-zero exit code")
            .PermitIf(Trigger.ProcessExit, State.ExitedKilled,
                () => _killed.Value && _process!.HasExited,
                "Killed");

        _processStateMachine
            .Configure(State.StartFailed)
            .Permit(Trigger.Start, State.Running);

        _processStateMachine
            .Configure(State.ExitedSuccessfully)
            .Permit(Trigger.Start, State.Running);

        _processStateMachine
            .Configure(State.ExitedKilled)
            .Permit(Trigger.Start, State.Running);

        _processStateMachine
            .Configure(State.ExitedWithError)
            .Permit(Trigger.Start, State.Running);

        _processStateMachine.OnTransitioned(transition =>
        {
            _logger.LogInformation("State transition from {Source} to {Destination}", transition.Source, transition.Destination);
            StateChanged?.Invoke(transition.Destination);
        });
    }

    /// <summary>
    ///     Contains the caught exception in the event a process failed to
    ///     be launched.
    /// </summary>
    public Exception? OnStartException { get; private set; }

    /// <summary>
    ///     Information about the launched process.
    /// </summary>
    public IProcessInfo? ProcessInfo { get; private set; }

    public State CurrentState => _processStateMachine.State;

    /// <summary>
    ///     Raised when the process emits console data.
    /// </summary>
    public event Action<string?>? OutputDataReceived;

    /// <summary>
    ///     Raised when the process emits stderr console data.
    /// </summary>
    public event Action<string?>? ErrorDataReceived;

    /// <summary>
    ///     Raised when the process state has changed.
    /// </summary>
    public event Action<State>? StateChanged;

    public string GetDotGraph() => UmlDotGraph.Format(_processStateMachine.GetInfo());

    /// <summary>
    ///     Starts the process.
    /// </summary>
    public Task Start()
        => _taskQueue.Enqueue(() =>
        {
            _killed.Set(false);
            _processStateMachine.Fire(Trigger.Start);
        });

    /// <summary>
    ///     Initiates a process stop. If a timeout is supplied (and greater
    ///     than 0ms), it will attempt a "co-operative" shutdown by
    ///     signalling an EXIT command to the process. The process needs to
    ///     support such signalling and needs to complete within the timeout
    ///     otherwise the process will be terminated via Kill(). The maximum
    ///     recommended timeout is 25 seconds. This is 5 seconds less than
    ///     default 30 seconds that windows will consider a service to be
    ///     'hung'.
    /// </summary>
    /// <param name="timeout"></param>
    /// <returns></returns>
    public async Task Stop(TimeSpan? timeout = null) =>
        await await _taskQueue
            .Enqueue(() => _processStateMachine.FireAsync(_stopTrigger, timeout))
            .ConfigureAwait(false);

    private void OnStart()
    {
        OnStartException = null;
        try
        {
            var processStartInfo = new ProcessStartInfo(_processPath)
            {
                Arguments = _arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = _captureStdErr,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _workingDirectory
            };

            // Copy over environment variables
            if (_environmentVariables != null)
            {
                foreach (string key in _environmentVariables.Keys)
                {
                    processStartInfo.EnvironmentVariables[key] = _environmentVariables[key];
                }
            }

            // Start the process and capture it's output.
            _process = new Process
            {
                StartInfo = processStartInfo,
                EnableRaisingEvents = true
            };
            _process.OutputDataReceived += (_, args) => OutputDataReceived?.Invoke(args.Data);
            if (_captureStdErr)
            {
                _process.ErrorDataReceived += (_, args) => ErrorDataReceived?.Invoke(args.Data);
            }
            _process.Exited += (sender, args) =>
            {
                _taskQueue.Enqueue(() =>
                {
                    _processStateMachine.Fire(Trigger.ProcessExit);
                });
            };
            _process.Start();
            _process.BeginOutputReadLine();
            if (_captureStdErr)
            {
                _process.BeginErrorReadLine();
            }

            ProcessInfo = new ProcessInfo(_process);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start process {ProcessPath}", _processPath);
            _processStateMachine.Fire(_startErrorTrigger, ex);
        }
    }

    private void OnStartError(Exception ex)
    {
        OnStartException = ex;
        _process?.Dispose();
        ProcessInfo = null;
    }

    private async Task OnStop(TimeSpan? timeout)
    {
        if (!timeout.HasValue || timeout.Value <= TimeSpan.Zero)
        {
            try
            {
                _logger.LogInformation("Killing process {ProcessId}", _process!.Id);
                _killed.Set(true);
                _process!.Kill();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Exception occurred attempting to kill process {ProcessId}. This may occur if there is "
                    + "a race condition where process has already exited and an attempt to kill it.",
                    _process!.Id);
            }
        }
        else
        {
            try
            {
                var sw = Stopwatch.StartNew();

                var exited = this.WhenStateIs(State.ExitedSuccessfully);
                var exitedWithError = this.WhenStateIs(State.ExitedWithError);

                var signaled = await CooperativeShutdown
                    .TrySignalExit(ProcessInfo!.Id, _loggerFactory).TimeoutAfter(timeout.Value)
                    .ConfigureAwait(false);

                if (!signaled)
                {
                    throw new TimeoutException("Cooperative shutdown signal failed.");
                }

                var remaining = timeout.Value - sw.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException("Timeout budget exhausted after signaling exit.");
                }

                await Task
                    .WhenAny(exited, exitedWithError)
                    .TimeoutAfter(remaining)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                try
                {
                    _logger.LogWarning(
                        "Timed out waiting to signal the process to exit or the "
                        + "process {ProcessPath} ({ProcessId}) did not shutdown in "
                        + "the given time ({Timeout})",
                        _processPath,
                        _process!.Id,
                        timeout);
                    _killed.Set(true);
                    _process!.Kill();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Exception occurred attempting to kill process {ProcessId}. This may occur "
                        + "in a race condition where the process has exited, a timeout waiting for the exit, "
                        + "and the attempt to kill it.",
                        _process!.Id);
                }
            }
        }
    }

    public void Dispose()
    {
        _taskQueue?.Dispose();
        _process?.Dispose();
    }
}
