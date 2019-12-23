using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using LittleForker.Logging;

namespace LittleForker
{
    /// <summary>
    ///     Allows a process to be co-operatively shut down (as opposed the more
    ///     brutal Process.Kill()
    /// </summary>
    public static class CooperativeShutdown
    {
        private static readonly ILog Logger = LogProvider.GetCurrentClassLogger();

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
        /// <param name="onError">A method to be called if an error occurs while listening</param>
        /// <returns>
        ///     A disposable representing the named pipe listener.
        /// </returns>
        public static Task<IDisposable> Listen(Action shutdownRequested, Action<Exception> onError = default)
        {
            var listener = new CooperativeShutdownListener(
                GetPipeName(Process.GetCurrentProcess().Id),
                shutdownRequested);
            
            Task.Run(async () =>
            {
                try
                {
                    await listener.Listen();
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
        /// <returns>A task represnting the operation.</returns>
        // TODO Should exceptions rethrow or should we let the caller that the signalling failed i.e. Task<book>?
        public static async Task SignalExit(int processId)
        {
            var pipeName = GetPipeName(processId);
            using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                try
                {
                    await pipe.ConnectAsync((int)TimeSpan.FromSeconds(3).TotalMilliseconds);
                    var streamWriter = new StreamWriter(pipe);
                    var streamReader = new StreamReader(pipe, true);
                    Logger.Info($"Signalling EXIT to client on pipe {pipeName}...");
                    await SignalExit(streamWriter, streamReader).TimeoutAfter(TimeSpan.FromSeconds(3));
                    Logger.Info($"Signalling EXIT to client on pipe {pipeName} successful.");
                }
                catch (IOException ex)
                {
                    Logger.ErrorException($"Failed to signal EXIT to client on pipe {pipeName}.", ex);
                    
                }
                catch (TimeoutException ex)
                {
                    Logger.ErrorException($"Timeout signalling EXIT on pipe {pipeName}.", ex);
                }
                catch (Exception ex)
                {
                    Logger.ErrorException($"Failed to signal EXIT to client on pipe {pipeName}.", ex);
                }
            }
        }

        private static async Task SignalExit(TextWriter streamWriter, TextReader streamReader)
        {
            await streamWriter.WriteLineAsync("EXIT");
            await streamWriter.FlushAsync();
            await streamReader.ReadLineAsync().TimeoutAfter(TimeSpan.FromSeconds(3)); // Reads an 'OK'.
        }

        private sealed class CooperativeShutdownListener : IDisposable
        {
            private readonly string _pipeName;
            private readonly Action _shutdownRequested;
            private readonly CancellationTokenSource _stopListening;

            internal CooperativeShutdownListener(
                string pipeName,
                Action shutdownRequested)
            {
                _pipeName = pipeName;
                _shutdownRequested = shutdownRequested;
                _stopListening = new CancellationTokenSource();
            }

            internal async Task Listen()
            {
                while (!_stopListening.IsCancellationRequested)
                {
                    // message transmission mode is not supported on Unix
                    var pipe = new NamedPipeServerStream(_pipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.None);

                    Logger.Info($"Listening on pipe '{_pipeName}'.");

                    await pipe.WaitForConnectionAsync(_stopListening.Token);
                    Logger.Info($"Client connected to pipe '{_pipeName}'.");

                    try
                    {
                        using (var reader = new StreamReader(pipe))
                        {
                            using (var writer = new StreamWriter(pipe) { AutoFlush = true })
                            {
                                while (true)
                                {
                                    // a pipe can get disconnected after OS pipes enumeration as well
                                    if (!pipe.IsConnected)
                                    {
                                        Logger.Debug($"Pipe {_pipeName} connection is broken re-connecting");
                                        break;
                                    }

                                    var s = await reader.ReadLineAsync().WithCancellation(_stopListening.Token);

                                    if (s != "EXIT")
                                    {
                                        continue;
                                    }

                                    Logger.Info($"Received command from server: {s}");

                                    await writer.WriteLineAsync("OK");
                                    Logger.Info("Responded with OK");

                                    Logger.Info("Raising exit request...");
                                    _shutdownRequested();

                                    return;
                                }
                            }
                        }
                    }
                    catch (IOException ex)
                    {
                        // As the pipe connection should be restored this exception should not be considered as terminating
                        Logger.Debug(ex, "Pipe connection failed");
                    }
                }
            }

            public void Dispose()
            {
                _stopListening.Cancel();
            }
        }
    }
}