using System.Collections.Specialized;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Stateless;
using Stateless.Graph;

namespace LittleForker;

/// <summary>
///     Launches an process and tracks it's lifecycle .
/// </summary>
public class ProcessSupervisor : IDisposable
{
    private readonly ILogger                                                       _logger;
    private readonly string                                                        _arguments;
    private readonly StringDictionary                                              _environmentVariables;
    private readonly bool                                                          _captureStdErr;
    private readonly string                                                        _processPath;
    private readonly StateMachine<State, Trigger>.TriggerWithParameters<Exception> _startErrorTrigger;
    private readonly StateMachine<State, Trigger>.TriggerWithParameters<TimeSpan?> _stopTrigger;
    private readonly StateMachine<State, Trigger>                                  _processStateMachine = new(State.NotStarted, FiringMode.Immediate);
    private readonly string                                                        _workingDirectory;
    private          Process                                                       _process;
    private readonly ProcessSupervisorSettings                                     _settings;
    private readonly ILoggerFactory                                                _loggerFactory;
    private          bool                                                          _killed;
    private readonly TaskQueue                                                     _taskQueue = new();

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
        ExitedUnexpectedly,
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
    ///     Initializes a new instance of <see cref="ProcessSupervisor"/>
    /// </summary>
    /// <param name="workingDirectory">
    ///     The working directory to start the process in.
    /// </param>
    /// <param name="processPath">
    ///     The path to the process.
    /// </param>
    /// <param name="processRunType">
    ///     The process run type.
    /// </param>
    /// <param name="settings"></param>
    /// <param name="loggerFactory">
    ///     A logger factory.
    /// </param>
    /// <param name="arguments">
    ///     Arguments to be passed to the process.
    /// </param>
    /// <param name="environmentVariables">
    ///     Environment variables that are set before the process starts.
    /// </param>
    /// <param name="captureStdErr">
    ///     A flag to indicated whether to capture standard error output.
    /// </param>
    public ProcessSupervisor(
        ProcessSupervisorSettings settings,
        ILoggerFactory            loggerFactory)
    {
        _settings        = settings;
        _loggerFactory   = loggerFactory;

        _logger = loggerFactory.CreateLogger($"{nameof(LittleForker)}.{nameof(ProcessSupervisor)}-{settings.ProcessPath}");

        _processStateMachine
            .Configure(State.NotStarted)
            .Permit(Trigger.Start, State.Running);

        _startErrorTrigger = _processStateMachine.SetTriggerParameters<Exception>(Trigger.StartError);
        _stopTrigger       = _processStateMachine.SetTriggerParameters<TimeSpan?>(Trigger.Stop);

        _processStateMachine
            .Configure(State.Running)
            .OnEntryFrom(Trigger.Start, OnStart)
            .PermitIf(
                Trigger.ProcessExit,
                State.ExitedUnexpectedly,
                () => _process.HasExited,
                "NonTerminating but exited.")
            .Permit(Trigger.Stop, State.Stopping)
            .Permit(Trigger.StartError, State.StartFailed);

        _processStateMachine
            .Configure(State.StartFailed)
            .OnEntryFrom(_startErrorTrigger, OnStartError);

        _processStateMachine
            .Configure(State.Stopping)
            .OnEntryFromAsync(_stopTrigger, OnStop)
            .PermitIf(Trigger.ProcessExit, State.ExitedSuccessfully,
                () => !_killed 
                      && _process.HasExited
                      && _process.ExitCode == 0,
                "NonTerminating and shut down cleanly")
            .PermitIf(Trigger.ProcessExit, State.ExitedWithError,
                () => !_killed
                      && _process.HasExited
                      && _process.ExitCode != 0,
                "NonTerminating and shut down with non-zero exit code")
            .PermitIf(Trigger.ProcessExit, State.ExitedKilled,
                () => _killed
                      && _process.HasExited
                      && _process.ExitCode != 0,
                "NonTerminating and killed.");

        _processStateMachine
            .Configure(State.StartFailed)
            .Permit(Trigger.Start, State.Running);

        _processStateMachine
            .Configure(State.ExitedSuccessfully)
            .Permit(Trigger.Start, State.Running);

        _processStateMachine
            .Configure(State.ExitedUnexpectedly)
            .Permit(Trigger.Start, State.Running);

        _processStateMachine
            .Configure(State.ExitedKilled)
            .Permit(Trigger.Start, State.Running);

        _processStateMachine.OnTransitioned(transition =>
        {
            _logger.LogInformation($"State transition from {transition.Source} to {transition.Destination}");
            StateChanged?.Invoke(transition.Destination);
        });
    }

    /// <summary>
    ///     Contains the caught exception in the event a process failed to
    ///     be launched.
    /// </summary>
    public Exception OnStartException { get; private set; }

    /// <summary>
    ///     Information about the launched process.
    /// </summary>
    public IProcessInfo? ProcessInfo { get; private set; }

    public State CurrentState => _processStateMachine.State;

    /// <summary>
    ///     Raised when the process emits console data.
    /// </summary>
    public event Action<string> OutputDataReceived;

    /// <summary>
    ///     Raised when the process emits stderr console data.
    /// </summary>
    public event Action<string> ErrorDataReceived;

    /// <summary>
    ///     Raised when the process state has changed.
    /// </summary>
    public event Action<State> StateChanged;

    public string GetDotGraph() => UmlDotGraph.Format(_processStateMachine.GetInfo());

    /// <summary>
    ///     Starts the process.
    /// </summary>
    public Task Start() 
        => _taskQueue.Enqueue(() =>
        {
            _killed = false;
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
    public async Task Stop(TimeSpan? timeout = null)
    {
        await await _taskQueue
            .Enqueue(() => _processStateMachine.FireAsync(_stopTrigger, timeout))
            .ConfigureAwait(false);
    }

    private void OnStart()
    {
        OnStartException = null;
        try
        {
            var processStartInfo = new ProcessStartInfo(_processPath)
            {
                Arguments              = _arguments,
                RedirectStandardOutput = true,
                RedirectStandardError  = _captureStdErr,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                WorkingDirectory       = _workingDirectory
            };

            // Copy over environment variables
            foreach (string key in _environmentVariables.Keys)
            {
                processStartInfo.EnvironmentVariables[key] = _environmentVariables[key];
            }
            

            // Start the process and capture it's output.
            _process = new Process
            {
                StartInfo           = processStartInfo,
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
            _logger.LogError(ex, $"Failed to start process {_processPath}");
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
                _logger.LogInformation($"Killing process {_process.Id}");
                _killed = true;
                _process.Kill();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    $"Exception occurred attempting to kill process {_process.Id}. This may if the " +
                    "in the a race condition where process has already exited and an attempt to kill it.");
            }
        }
        else
        {
            try
            {
                // Signal process to exit. If no response from process (busy or
                // doesn't support signaling, then this will timeout. Note: a
                // process acknowledging that it has received an EXIT signal
                // only means the process has _started_ to shut down.

                var exited          = this.WhenStateIs(State.ExitedSuccessfully);
                var exitedWithError = this.WhenStateIs(State.ExitedWithError);

                await CooperativeShutdown
                    .SignalExit(ProcessInfo.Id, _loggerFactory).TimeoutAfter(timeout.Value)
                    .ConfigureAwait(false);

                await Task
                    .WhenAny(exited, exitedWithError)
                    .TimeoutAfter(timeout.Value)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Process doesn't support EXIT signal OR it didn't respond in
                // time so Kill it. Note: still a race condition - the EXIT
                // signal may have been received but just not acknowledged in
                // time.
                try
                {
                    _logger.LogWarning(
                        $"Timed out waiting to signal the process to exit or the "             +
                        $"process {_process.ProcessName} ({_process.Id}) did not shutdown in " +
                        $"the given time ({timeout})");
                    _killed = true;
                    _process.Kill();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        $"Exception occurred attempting to kill process {_process.Id}. This may occur "         +
                        "in the a race condition where the process has exited, a timeout waiting for the exit," +
                        "and the attempt to kill it.");
                }
            }
        }
    }

    public void Dispose()
    {
        _process?.Dispose();
        _taskQueue?.Dispose();
    }
}