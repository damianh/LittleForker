using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Threading.Tasks;
using LittleForker.Logging;
using Stateless;
using Stateless.Graph;

namespace LittleForker
{
    /// <summary>
    ///     Launches an process and tracks it's lifecycle .
    /// </summary>
    public class ProcessSupervisor
    {
        private readonly ILog _logger;
        private readonly string _arguments;
        private readonly StringDictionary _environmentVariables;
        private readonly string _processPath;
        private readonly StateMachine<State, Trigger>.TriggerWithParameters<Exception> _startErrorTrigger;
        private readonly StateMachine<State, Trigger>.TriggerWithParameters<TimeSpan?> _stopTrigger;
        private readonly StateMachine<State, Trigger> _processStateMachine
            = new StateMachine<State, Trigger>(State.NotStarted, FiringMode.Queued);
        private readonly string _workingDirectory;
        private Process _process;
        private readonly object _lockObject = new object();

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
            ExitedUnexpectedly
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
        /// <param name="arguments">
        ///     Arguments to be passed to the process.
        /// </param>
        /// <param name="environmentVariables">
        ///     Environment variables that are set before the process starts.
        /// </param>
        public ProcessSupervisor(
            ProcessRunType processRunType,
            string workingDirectory,
            string processPath,
            string arguments = null,
            StringDictionary environmentVariables = null)
        {
            _workingDirectory = workingDirectory;
            _processPath = processPath;
            _arguments = arguments ?? string.Empty;
            _environmentVariables = environmentVariables;

            _logger = LogProvider.GetLogger($"ProcessSupervisor-{processPath}");

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
                    () => processRunType == ProcessRunType.SelfTerminating && _process.ExitCode == 0,
                    "SelfTerminating && ExitCode==0")
                .PermitIf(
                    Trigger.ProcessExit,
                    State.ExitedWithError,
                    () => processRunType == ProcessRunType.SelfTerminating && _process.ExitCode != 0,
                    "SelfTerminating && ExitCode!=0")
                .PermitIf(
                    Trigger.ProcessExit,
                    State.ExitedUnexpectedly,
                    () => processRunType == ProcessRunType.NonTerminating,
                    "NonTerminating")
                .Permit(Trigger.Stop, State.Stopping)
                .Permit(Trigger.StartError, State.StartFailed);

            _processStateMachine
                .Configure(State.StartFailed)
                .OnEntryFrom(_startErrorTrigger, OnStartError);

            _processStateMachine
                .Configure(State.Stopping)
                .Permit(Trigger.ProcessExit, State.ExitedSuccessfully)
                .OnEntryFromAsync(_stopTrigger, OnStop);

            _processStateMachine
                .Configure(State.StartFailed)
                .Permit(Trigger.Start, State.Running);

            _processStateMachine
                .Configure(State.ExitedSuccessfully)
                .Permit(Trigger.Start, State.Running);

            _processStateMachine
                .Configure(State.ExitedUnexpectedly)
                .Permit(Trigger.Start, State.Running);

            _processStateMachine.OnTransitioned(transition =>
            {
                _logger.Info($"State transition from {transition.Source} to {transition.Destination}");
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
        public IProcessInfo ProcessInfo { get; private set; }

        public State CurrentState
        {
            get
            {
                lock (_lockObject)
                { 
                    return _processStateMachine.State;
                }
            }
        }

        /// <summary>
        ///     Raised when the process emits console data.
        /// </summary>
        public event Action<string> OutputDataReceived;

        /// <summary>
        ///     Raised when the process state has changed.
        /// </summary>
        public event Action<State> StateChanged;

        public string GetDotGraph() => UmlDotGraph.Format(_processStateMachine.GetInfo());

        /// <summary>
        ///     Starts the process.
        /// </summary>
        public void Start()
        {
            lock (_lockObject)
            {
                _processStateMachine.Fire(Trigger.Start);
            }
        }

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
        public Task Stop(TimeSpan? timeout = null)
        {
            lock (_lockObject)
            {
                return _processStateMachine.FireAsync(_stopTrigger, timeout);
            }
        }

        private void OnStart()
        {
            OnStartException = null;
            try
            {
                var processStartInfo = new ProcessStartInfo(_processPath)
                {
                    Arguments = _arguments,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = _workingDirectory
                };

                // Copy over environment variables
                if (_environmentVariables != null)
                {
                    foreach (string key in _environmentVariables.Keys)
                    {
                        processStartInfo.EnvironmentVariables.Add(key, _environmentVariables[key]);
                    }
                }

                // Start the process and capture it's output.
                _process = new Process
                {
                    StartInfo = processStartInfo,
                    EnableRaisingEvents = true
                };
                _process.OutputDataReceived += (_, args) => OutputDataReceived?.Invoke(args.Data);
                _process.Exited += (sender, args) =>
                {
                    lock (_lockObject)
                    {
                        _processStateMachine.FireAsync(Trigger.ProcessExit);
                    }
                }; // Multi-threaded access ?
                _process.Start();
                _process.BeginOutputReadLine();
                ProcessInfo = new ProcessInfo(_process);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("Failed to start process.", ex);
                lock (_lockObject)
                {
                    _processStateMachine.Fire(_startErrorTrigger, ex);
                }
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
                    _logger.Info($"Killing process {_process.Id}");
                    _process.Kill();
                }
                catch (Exception ex)
                {
                    _logger.WarnException(
                        $"Exception occurred attempting to kill process {_process.Id}. This may if the " +
                        "in the a race condition where process has already exited and an attempt to kill it.",
                        ex);
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

                    // TODO detect if the app hasn't shut down in the allotted time and if so, kill it.
                    await CooperativeShutdown.SignalExit(ProcessInfo.Id).TimeoutAfter(timeout.Value);
                }
                catch (TimeoutException)
                {
                    // Process doesn't support EXIT signal OR it didn't response in
                    // time so Kill it. Note: still a race condition - the EXIT
                    // signal may have been received but just not acknowledged in
                    // time.
                    try
                    {
                        _process.Kill();
                    }
                    catch (Exception ex)
                    {
                        _logger.WarnException(
                            $"Exception occurred attempting to kill process {_process.Id}. This may if the " +
                            "in the a race condition where process has already exited and an attempt to kill it.",
                            ex);
                    }
                }
            }
        }
    }
}