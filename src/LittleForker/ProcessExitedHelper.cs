using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace LittleForker;

/// <summary>
///     Helper that raises event when the process has exited. A wrapper around
///     Process.Exited with some error handling and logging.
/// </summary>
public sealed class ProcessExitedHelper : IDisposable
{
    private          int     _processExitedRaised;
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
    /// <param name="loggerFactory">
    ///     A logger.
    /// </param>
    public ProcessExitedHelper(
        int                         processId,
        Action<ProcessExitedHelper> processExited,
        ILoggerFactory              loggerFactory)
    {
        ProcessId = processId;
        var logger = loggerFactory.CreateLogger($"{nameof(LittleForker)}.{nameof(ProcessExitedHelper)}");

        try
        {
            _process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            logger.LogError("Process with Id {ProcessId} was not found.", processId);
            OnProcessExit();
            return;
        }

        logger.LogInformation("Process with Id {ProcessId} found.", processId);
        try
        {
            _process.EnableRaisingEvents = true;
            _process.Exited += (_, __) =>
            {
                logger.LogInformation("Parent process with Id {ProcessId} exited.", processId);
                OnProcessExit();
            };
        }
        // Race condition: this may be thrown if the process has already exited before
        // attaching to the Exited event
        catch (InvalidOperationException ex) 
        {
            logger.LogInformation(ex, "Process with Id {ProcessId} has already exited.", processId);
            OnProcessExit();
        }

        if (_process.HasExited)
        {
            logger.LogInformation("Process with Id {ProcessId} has already exited.", processId);
            OnProcessExit();
        }

        void OnProcessExit()
        {
            if (Interlocked.CompareExchange(ref _processExitedRaised, 1, 0) == 0) // Ensure raised once
            {
                logger.LogInformation("Raising process exited.");
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