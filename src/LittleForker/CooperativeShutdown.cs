using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace LittleForker;

/// <summary>
///     Allows a process to be co-cooperatively shut down (as opposed the more
///     brutal Process.Kill()
/// </summary>
public static class CooperativeShutdown
{
    /// <summary>
    ///     The pipe name a process will listen on for a EXIT signal.
    /// </summary>
    /// <param name="processId">The process ID process listening.</param>
    /// <returns>A generated pipe name.</returns>
    public static string GetPipeName(int processId) => $"LittleForker-{processId}";

    /// <summary>
    ///     The pipe name a process will listen on for a EXIT signal, including a security nonce.
    /// </summary>
    /// <param name="processId">The process ID process listening.</param>
    /// <param name="nonce">A shared secret nonce known to both parent and child.</param>
    /// <returns>A generated pipe name that includes the nonce.</returns>
    public static string GetPipeName(int processId, string nonce) => $"LittleForker-{processId}-{nonce}";

    /// <summary>
    ///     Creates a listener for cooperative shutdown.
    /// </summary>
    /// <param name="shutdownRequested">
    ///     The callback that is invoked when cooperative shutdown has been
    ///     requested.
    /// </param>
    /// <param name="loggerFactory">
    ///     A logger factory.
    /// </param>
    /// <param name="onError">A method to be called if an error occurs while listening</param>
    /// <returns>
    ///     A disposable representing the named pipe listener.
    /// </returns>
    public static Task<IDisposable> Listen(Action shutdownRequested, ILoggerFactory loggerFactory, Action<Exception> onError = default)
    {
        return Listen(shutdownRequested, loggerFactory, nonce: null, onError: onError);
    }

    /// <summary>
    ///     Creates a listener for cooperative shutdown with an optional security nonce.
    /// </summary>
    /// <param name="shutdownRequested">
    ///     The callback that is invoked when cooperative shutdown has been
    ///     requested.
    /// </param>
    /// <param name="loggerFactory">
    ///     A logger factory.
    /// </param>
    /// <param name="nonce">
    ///     An optional security nonce shared between parent and child process.
    ///     When provided, the pipe name includes the nonce, preventing other local
    ///     processes from sending EXIT signals.
    /// </param>
    /// <param name="onError">A method to be called if an error occurs while listening</param>
    /// <returns>
    ///     A disposable representing the named pipe listener. The pipe server is started
    ///     on a background thread and may not be ready for connections immediately after
    ///     this method returns.
    /// </returns>
    public static Task<IDisposable> Listen(
        Action shutdownRequested,
        ILoggerFactory loggerFactory,
        string nonce,
        Action<Exception> onError = default)
    {
        var processId = Process.GetCurrentProcess().Id;
        var pipeName = nonce != null
            ? GetPipeName(processId, nonce)
            : GetPipeName(processId);

        var listener = new CooperativeShutdownListener(
            pipeName,
            shutdownRequested,
            loggerFactory.CreateLogger($"{nameof(LittleForker)}.{nameof(CooperativeShutdown)}"));
            
        Task.Run(async () =>
        {
            try
            {
                await listener.Listen().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal disposal — not an error.
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        });

        return Task.FromResult((IDisposable)listener);
    }

    /// <summary>
    ///     Signals to a process to shut down.
    /// </summary>
    /// <param name="processId">The process ID to signal too.</param>
    /// <param name="loggerFactory">A logger factory.</param>
    /// <returns>A task representing the operation.</returns>
    public static async Task SignalExit(int processId, ILoggerFactory loggerFactory)
    {
        await TrySignalExit(processId, loggerFactory).ConfigureAwait(false);
    }

    /// <summary>
    ///     Signals to a process to shut down using a security nonce.
    /// </summary>
    /// <param name="processId">The process ID to signal too.</param>
    /// <param name="loggerFactory">A logger factory.</param>
    /// <param name="nonce">The security nonce shared between parent and child process.</param>
    /// <returns>A task representing the operation.</returns>
    public static async Task SignalExit(int processId, ILoggerFactory loggerFactory, string nonce)
    {
        await TrySignalExit(processId, loggerFactory, nonce).ConfigureAwait(false);
    }

    /// <summary>
    ///     Signals to a process to shut down, returning whether the signal was delivered.
    /// </summary>
    /// <param name="processId">The process ID to signal too.</param>
    /// <param name="loggerFactory">A logger factory.</param>
    /// <returns><c>true</c> if the EXIT signal was successfully delivered; <c>false</c> on failure.</returns>
    internal static Task<bool> TrySignalExit(int processId, ILoggerFactory loggerFactory)
    {
        return TrySignalExitCore(processId, loggerFactory, GetPipeName(processId));
    }

    /// <summary>
    ///     Signals to a process to shut down using a security nonce, returning whether the signal was delivered.
    /// </summary>
    /// <param name="processId">The process ID to signal too.</param>
    /// <param name="loggerFactory">A logger factory.</param>
    /// <param name="nonce">The security nonce shared between parent and child process.</param>
    /// <returns><c>true</c> if the EXIT signal was successfully delivered; <c>false</c> on failure.</returns>
    internal static Task<bool> TrySignalExit(int processId, ILoggerFactory loggerFactory, string nonce)
    {
        return TrySignalExitCore(processId, loggerFactory, GetPipeName(processId, nonce));
    }

    private static async Task<bool> TrySignalExitCore(int processId, ILoggerFactory loggerFactory, string pipeName)
    {
        var logger = loggerFactory.CreateLogger($"{nameof(LittleForker)}.{nameof(CooperativeShutdown)}");
        using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            try
            {
                await pipe.ConnectAsync((int)TimeSpan.FromSeconds(3).TotalMilliseconds).ConfigureAwait(false);
                var streamWriter = new StreamWriter(pipe);
                var streamReader = new StreamReader(pipe, true);
                logger.LogInformation("Signalling EXIT to client on pipe {PipeName}...", pipeName);
                await SignalExit(streamWriter, streamReader).TimeoutAfter(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                logger.LogInformation("Signalling EXIT to client on pipe {PipeName} successful.", pipeName);
                return true;
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "Failed to signal EXIT to client on pipe {PipeName}.", pipeName);
                return false;
            }
            catch (TimeoutException ex)
            {
                logger.LogError(ex, "Timeout signalling EXIT on pipe {PipeName}.", pipeName);
                return false;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to signal EXIT to client on pipe {PipeName}.", pipeName);
                return false;
            }
        }
    }

    private static async Task SignalExit(TextWriter streamWriter, TextReader streamReader)
    {
        await streamWriter.WriteLineAsync("EXIT").ConfigureAwait(false);
        await streamWriter.FlushAsync().ConfigureAwait(false);
        await streamReader.ReadLineAsync().TimeoutAfter(TimeSpan.FromSeconds(3)).ConfigureAwait(false); // Reads an 'OK'.
    }

    private sealed class CooperativeShutdownListener : IDisposable
    {
        private readonly string                  _pipeName;
        private readonly Action                  _shutdownRequested;
        private readonly ILogger                 _logger;
        private readonly CancellationTokenSource _stopListening;

        internal CooperativeShutdownListener(
            string  pipeName,
            Action  shutdownRequested,
            ILogger logger)
        {
            _pipeName          = pipeName;
            _shutdownRequested = shutdownRequested;
            _logger            = logger;
            _stopListening     = new CancellationTokenSource();
        }

        internal async Task Listen()
        {
            while (!_stopListening.IsCancellationRequested)
            {
                // message transmission mode is not supported on Unix
                var pipe = new NamedPipeServerStream(_pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None);

                try
                {
                    _logger.LogInformation("Listening on pipe '{PipeName}'.", _pipeName);

                    await pipe
                        .WaitForConnectionAsync(_stopListening.Token)
                        .ConfigureAwait(false);

                    _logger.LogInformation("Client connected to pipe '{PipeName}'.", _pipeName);

                    using (var reader = new StreamReader(pipe))
                    {
                        using (var writer = new StreamWriter(pipe) { AutoFlush = true })
                        {
                            while (true)
                            {
                                // a pipe can get disconnected after OS pipes enumeration as well
                                if (!pipe.IsConnected)
                                {
                                    _logger.LogDebug("Pipe {PipeName} connection is broken re-connecting", _pipeName);
                                    break;
                                }

                                var s = await reader.ReadLineAsync().WithCancellation(_stopListening.Token)
                                    .ConfigureAwait(false);

                                if (s != "EXIT")
                                {
                                    continue;
                                }

                                _logger.LogInformation("Received command from server: {Command}", s);

                                await writer.WriteLineAsync("OK").ConfigureAwait(false);
                                _logger.LogInformation("Responded with OK");

                                _logger.LogInformation("Raising exit request...");
                                _shutdownRequested();

                                return;
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal disposal path — break cleanly without re-throwing.
                    break;
                }
                catch (IOException ex)
                {
                    // As the pipe connection should be restored this exception should not be considered as terminating
                    _logger.LogDebug(ex, "Pipe connection failed");
                }
                finally
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        public void Dispose()
        {
            _stopListening.Cancel();
        }
    }
}