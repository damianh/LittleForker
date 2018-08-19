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
        /// <returns>
        ///     A disposable representing the named pipe listener.
        /// </returns>
        public static async Task<IDisposable> Listen(Action shutdownRequested)
        {
            var listener = new CooperativeShutdownListener(
                GetPipeName(Process.GetCurrentProcess().Id),
                shutdownRequested);
            await listener.Listen();
            return listener;
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
            private static readonly ILog Logger = LogProvider.GetCurrentClassLogger();
            private readonly CancellationTokenSource _stopListening;
            private TaskCompletionSource<int> _listening;

            internal CooperativeShutdownListener(
                string pipeName,
                Action shutdownRequested)
            {
                _pipeName = pipeName;
                _shutdownRequested = shutdownRequested;
                _stopListening = new CancellationTokenSource();
            }

            internal Task Listen()
            {
                _listening = new TaskCompletionSource<int>();
                Task.Run(async () =>
                {
                    NamedPipeServerStream pipe;
                    try
                    {
                        pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut);
                    }
                    catch (Exception ex)
                    {
                        _listening.SetException(ex);
                        throw;
                    }
                    _listening.SetResult(0);
                    Logger.Info($"Listening on pipe '{_pipeName}'.");
                    await pipe.WaitForConnectionAsync(_stopListening.Token);
                    Logger.Info($"Client connected to pipe '{_pipeName}'.");
                    using (var reader = new StreamReader(pipe))
                    {
                        using (var writer = new StreamWriter(pipe))
                        {

                            var s = "";
                            while (s != "EXIT")
                            {
                                s = await reader.ReadLineAsync().WithCancellation(_stopListening.Token);
                                Logger.Info($"Received command from server: {s}");
                            }

                            await writer.WriteLineAsync("OK");
                            await writer.FlushAsync();
                            Logger.Info("Responded with OK");

                            Logger.Info("Raising exit request...");
                            _shutdownRequested();
                        }
                    }
                });
                return _listening.Task;
            }

            public void Dispose()
            {
                _stopListening.Cancel();
            }
        }
    }
}