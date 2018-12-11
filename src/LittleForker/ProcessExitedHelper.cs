using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using LittleForker.Logging;

namespace LittleForker
{
    /// <summary>
    ///     Helper that raises event when the process has exited. A wrapper around
    ///     Process.Exited with some error handling and logging.
    /// </summary>
    public sealed class ProcessExitedHelper : IDisposable
    {
        private static readonly ILog Logger = LogProvider.GetCurrentClassLogger();
        private int _processExitedRaised;
        private readonly Process _process;

        /// <summary>
        ///     Initializes a new instance of <see cref="ProcessExitedHelper"/>
        /// </summary>
        /// <param name="processId">
        ///     The process Id of the process to watch for exited.
        /// </param>
        /// <param name="processExited">
        ///     A callback that is invoked when process has exited or does not
        ///     exist with the <see cref="ProcessExitedHelper"/> instance as a
        ///     parameter.
        /// </param>
        public ProcessExitedHelper(
            int processId,
            Action<ProcessExitedHelper> processExited)
        {
            ProcessId = processId;
            _process = Process.GetProcesses().SingleOrDefault(pr => pr.Id == processId);
            if (_process == null)
            {
                Logger.Error($"Process with Id {processId} was not found.");
                OnProcessExit();
                return;
            }
            Logger.Info($"Process with Id {processId} found.");
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
                    processExited(this);
                }
            }
        }

        public int ProcessId { get; }

        public void Dispose()
        {
            _process?.Dispose();
        }
    }
}