// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

using System.Diagnostics;
using System.IO.Pipes;
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
    public static Task<IDisposable> Listen(Action shutdownRequested, ILoggerFactory loggerFactory, Action<Exception>? onError = default)
        => Listen(shutdownRequested, loggerFactory, nonce: null, onError: onError);

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
    ///     When provided, the listener validates the nonce in the wire protocol
    ///     before accepting an EXIT command.
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
        string? nonce,
        Action<Exception>? onError = default)
    {
        var processId = Process.GetCurrentProcess().Id;
        var pipeName = GetPipeName(processId);

        var listener = new CooperativeShutdownListener(
            pipeName,
            nonce,
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
        => await TrySignalExit(processId, loggerFactory).ConfigureAwait(false);

    /// <summary>
    ///     Signals to a process to shut down using a security nonce.
    /// </summary>
    /// <param name="processId">The process ID to signal too.</param>
    /// <param name="loggerFactory">A logger factory.</param>
    /// <param name="nonce">The security nonce shared between parent and child process.</param>
    /// <returns>A task representing the operation.</returns>
    public static async Task SignalExit(int processId, ILoggerFactory loggerFactory, string nonce)
        => await TrySignalExit(processId, loggerFactory, nonce).ConfigureAwait(false);

    /// <summary>
    ///     Signals to a process to shut down using an explicit pipe name.
    /// </summary>
    /// <param name="pipeName">The pipe name to signal on.</param>
    /// <param name="loggerFactory">A logger factory.</param>
    /// <returns>A task representing the operation.</returns>
    public static async Task SignalExit(string pipeName, ILoggerFactory loggerFactory)
        => await TrySignalExit(pipeName, loggerFactory).ConfigureAwait(false);

    /// <summary>
    ///     Signals to a process to shut down, returning whether the signal was delivered.
    /// </summary>
    /// <param name="processId">The process ID to signal too.</param>
    /// <param name="loggerFactory">A logger factory.</param>
    /// <returns><c>true</c> if the EXIT signal was successfully delivered; <c>false</c> on failure.</returns>
    internal static Task<bool> TrySignalExit(int processId, ILoggerFactory loggerFactory)
        => TrySignalExitCore(loggerFactory, GetPipeName(processId), nonce: null);

    /// <summary>
    ///     Signals to a process to shut down using a security nonce, returning whether the signal was delivered.
    /// </summary>
    /// <param name="processId">The process ID to signal too.</param>
    /// <param name="loggerFactory">A logger factory.</param>
    /// <param name="nonce">The security nonce shared between parent and child process.</param>
    /// <returns><c>true</c> if the EXIT signal was successfully delivered; <c>false</c> on failure.</returns>
    internal static Task<bool> TrySignalExit(int processId, ILoggerFactory loggerFactory, string nonce)
        => TrySignalExitCore(loggerFactory, GetPipeName(processId), nonce);

    /// <summary>
    ///     Signals to a process to shut down using an explicit pipe name, returning whether the signal was delivered.
    /// </summary>
    /// <param name="pipeName">The pipe name to signal on.</param>
    /// <param name="loggerFactory">A logger factory.</param>
    /// <returns><c>true</c> if the EXIT signal was successfully delivered; <c>false</c> on failure.</returns>
    internal static Task<bool> TrySignalExit(string pipeName, ILoggerFactory loggerFactory)
        => TrySignalExitCore(loggerFactory, pipeName, nonce: null);

    /// <summary>
    ///     Creates a <see cref="NamedPipeServerStream"/> restricted to the current user on Windows.
    ///     On non-Windows platforms, standard filesystem permissions apply.
    /// </summary>
    internal static NamedPipeServerStream CreateSecurePipeServer(string pipeName)
    {
        if (OperatingSystem.IsWindows())
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                PipeAccessRights.FullControl,
                System.Security.AccessControl.AccessControlType.Allow));
            return NamedPipeServerStreamAcl.Create(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.None,
                0, 0, security);
        }

        return new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None);
    }

    /// <summary>
    ///     Validates a received command against an optional nonce.
    ///     Returns <c>true</c> if the command is a valid EXIT signal.
    /// </summary>
    internal static bool IsValidExitCommand(string? command, string? expectedNonce)
    {
        if (command is null)
        {
            return false;
        }

        if (expectedNonce is null)
        {
            // No nonce configured — accept "EXIT" or "EXIT {anything}".
            return command == "EXIT" || command.StartsWith("EXIT ", StringComparison.Ordinal);
        }

        // Nonce configured — require exact match of "EXIT {nonce}".
        return command == $"EXIT {expectedNonce}";
    }

    private static async Task<bool> TrySignalExitCore(ILoggerFactory loggerFactory, string pipeName, string? nonce)
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
                await SignalExitCore(streamWriter, streamReader, nonce).TimeoutAfter(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
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

    private static async Task SignalExitCore(TextWriter streamWriter, TextReader streamReader, string? nonce)
    {
        var message = nonce is not null ? $"EXIT {nonce}" : "EXIT";
        await streamWriter.WriteLineAsync(message).ConfigureAwait(false);
        await streamWriter.FlushAsync().ConfigureAwait(false);
        await streamReader.ReadLineAsync().TimeoutAfter(TimeSpan.FromSeconds(3)).ConfigureAwait(false); // Reads an 'OK'.
    }

    private sealed class CooperativeShutdownListener : IDisposable
    {
        private readonly string _pipeName;
        private readonly string? _nonce;
        private readonly Action _shutdownRequested;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _stopListening;

        internal CooperativeShutdownListener(
            string pipeName,
            string? nonce,
            Action shutdownRequested,
            ILogger logger)
        {
            _pipeName = pipeName;
            _nonce = nonce;
            _shutdownRequested = shutdownRequested;
            _logger = logger;
            _stopListening = new CancellationTokenSource();
        }

        internal async Task Listen()
        {
            while (!_stopListening.IsCancellationRequested)
            {
                var pipe = CreateSecurePipeServer(_pipeName);

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

                                if (!IsValidExitCommand(s, _nonce))
                                {
                                    _logger.LogDebug("Received invalid or unrecognized command on pipe '{PipeName}': {Command}", _pipeName, s);
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

        public void Dispose() => _stopListening.Cancel();
    }
}
