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
        /// <param name="processId">
        ///     The process Id of the process to monitor.
        /// </param>
        /// <param name="processExited">
        ///     A callback that is invoked when process has exited or does not
        ///     exist.
        /// </param>
        public ProcessMonitor(
            int processId,
            Action<int> processExited)
        {
            _process = Process.GetProcesses().SingleOrDefault(pr => pr.Id == processId);
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