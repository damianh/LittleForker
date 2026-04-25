// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

using System.IO.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LittleForker;

/// <summary>
///     A hosted service that listens for a cooperative shutdown signal via a named pipe
///     and calls <see cref="IHostApplicationLifetime.StopApplication"/> when received.
/// </summary>
public sealed class CooperativeShutdownHostedService : BackgroundService
{
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<CooperativeShutdownHostedService> _logger;
    private readonly CooperativeShutdownHostedServiceOptions _options;

    public CooperativeShutdownHostedService(
        IHostApplicationLifetime applicationLifetime,
        IOptions<CooperativeShutdownHostedServiceOptions> options,
        ILogger<CooperativeShutdownHostedService> logger)
    {
        _applicationLifetime = applicationLifetime;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pipeName = ResolvePipeName();

        if (string.IsNullOrWhiteSpace(pipeName))
        {
            _logger.LogWarning("Pipe name could not be determined. Process will not listen for cooperative shutdown requests.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            // message transmission mode is not supported on Unix
            var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.None);

            try
            {
                _logger.LogInformation("Listening on pipe '{PipeName}'.", pipeName);

                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);

                _logger.LogInformation("Client connected to pipe '{PipeName}'.", pipeName);

                using var reader = new StreamReader(pipe);
                await using var writer = new StreamWriter(pipe) { AutoFlush = true };

                while (true)
                {
                    if (!pipe.IsConnected)
                    {
                        _logger.LogDebug("Pipe {PipeName} connection is broken, re-connecting.", pipeName);
                        break;
                    }

                    var command = await reader
                        .ReadLineAsync()
                        .WaitAsync(stoppingToken)
                        .ConfigureAwait(false);

                    if (command != "EXIT")
                    {
                        continue;
                    }

                    _logger.LogInformation("Received EXIT command on pipe '{PipeName}'.", pipeName);

                    await writer.WriteLineAsync("OK").ConfigureAwait(false);
                    _logger.LogInformation("Responded with OK.");

                    _logger.LogInformation("Requesting application stop.");
                    _applicationLifetime.StopApplication();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException ex)
            {
                _logger.LogDebug(ex, "Pipe connection failed, re-connecting.");
            }
            finally
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private string? ResolvePipeName()
    {
        if (!string.IsNullOrWhiteSpace(_options.PipeName))
        {
            return _options.PipeName;
        }

        var pid = Environment.ProcessId;
        return _options.Nonce != null
            ? CooperativeShutdown.GetPipeName(pid, _options.Nonce)
            : CooperativeShutdown.GetPipeName(pid);
    }
}
