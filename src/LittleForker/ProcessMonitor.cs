using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using LittleForker.Logging;

namespace LittleForker
{
    /// <summary>
    ///     Monitors a process and raises event when the process has exited.
    /// </summary>
    public sealed class ProcessMonitor : IDisposable
    {
        private static readonly ILog Logger = LogProvider.GetCurrentClassLogger();
        private int _processExitedRaised;
        private readonly Process _process;

        /// <summary>
        ///     Initializes a new instance of <see cref="ProcessMonitor"/>
        /// </summary>
        /// <param name="processExited">
        ///     A callback that is invoked when process has exited or does not
        ///     exist.
        /// </param>
        /// <param name="processId">
        ///     The process Id of the process to monitor. If it is not supplied
        ///     then environment variable 'ProcessIdEnvironmentVariable'
        ///     is checked.
        /// </param>
        /// <param name="raiseProcessExitedIfNoProcessId">
        ///     When True, raises <see cref="processExited"/> callback if neither
        ///     <see cref="processId"/> is supplied nor environment
        ///     variable is found.
        /// </param>
        public ProcessMonitor(
            Action<int?> processExited,
            int? processId = null,
            bool raiseProcessExitedIfNoProcessId = false)
        {
            if (!processId.HasValue)
            {
                if(int.TryParse(Environment.GetEnvironmentVariable(Constants.ProcessIdEnvironmentVariable), out var pid))
                {
                    Logger.Info($"Using Parent process Id {pid} from environment variable {Constants.ProcessIdEnvironmentVariable}.");
                    processId = pid;
                }
            }
            if (!processId.HasValue)
            {
                if (raiseProcessExitedIfNoProcessId)
                {
                    Logger.Info("No process Id found in ctor parameter nor environment.");
                    OnProcessExit();
                }
                else
                {
                    Logger.Warn("No process Id supplied; process monitoring disabled.");
                }
            }
            else 
            {
                _process = Process.GetProcesses().SingleOrDefault(pr => pr.Id == processId.Value);
                if (_process == null)
                {
                    Logger.Error($"Process with Id {processId} was not found.");
                    OnProcessExit();
                    return;
                }
                Logger.Info($"Process with Id {processId} found. Monitoring exited event.");
                try
                {
                    _process.EnableRaisingEvents = true;
                    _process.Exited += (_, __) =>
                    {
                        Logger.Info($"Parent process with Id {processId} exited.");
                        OnProcessExit();
                    };
                }
                // Race condition: this may be thrown if the process has already exited before
                // attaching to the Exited event
                catch (InvalidOperationException ex) 
                {
                    Logger.ErrorException($"Process with Id {processId} has already exited.", ex);
                    OnProcessExit();
                }

                if (_process.HasExited)
                {
                    Logger.Error($"Process with Id {processId} has already exited.");
                    OnProcessExit();
                }
            }

            void OnProcessExit()
            {
                if (Interlocked.CompareExchange(ref _processExitedRaised, 1, 0) == 0) // Ensure raised once
                {
                    Logger.Info("Raising process exited.");
                    processExited(processId);
                }
            }
        }

        public void Dispose()
        {
            _process?.Dispose();
        }
    }
}