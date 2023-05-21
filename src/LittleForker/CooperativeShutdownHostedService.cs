using Microsoft.Extensions.Hosting;
using System.IO.Pipes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;

namespace LittleForker;

public class CooperativeShutdownHostedService : BackgroundService
{
    private readonly IHostApplicationLifetime                  _applicationLifetime;
    private readonly ILogger<CooperativeShutdownHostedService> _logger;
    private readonly CooperativeShutdownHostedServiceOptions   _options;

    public CooperativeShutdownHostedService(
        IHostApplicationLifetime applicationLifetime, 
        IOptions<CooperativeShutdownHostedServiceOptions> options, 
        ILogger<CooperativeShutdownHostedService> logger)
    {
        _applicationLifetime = applicationLifetime;
        _logger              = logger;
        _options             = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.PipeName))
        {
            _logger.LogWarning("Pipe name not specified. Process will not be listening for " +
                               "cooperative shutdown requests.");
            return;
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            // message transmission mode is not supported on Unix
            var pipe = new NamedPipeServerStream(_options.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.None);

            _logger.LogInformation("Listening on pipe '{pipeName}'.", _options.PipeName);

            await pipe
                .WaitForConnectionAsync(stoppingToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Client connected to pipe '{pipeName}'.", _options.PipeName);

            try
            {
                using var       reader = new StreamReader(pipe);
                await using var writer = new StreamWriter(pipe) { AutoFlush = true };
                while (true)
                {
                    // a pipe can get disconnected after OS pipes enumeration as well
                    if (!pipe.IsConnected)
                    {
                        _logger.LogDebug("Pipe {pipeName} connection is broken, re-connecting.", _options.PipeName);
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

                    _logger.LogInformation("Received command from server: {s}", command);

                    await writer.WriteLineAsync("OK");
                    _logger.LogInformation("Responded with OK");

                    _logger.LogInformation("Raising exit request...");
                    _applicationLifetime.StopApplication();
                    return;
                }
            }
            catch (IOException ex)
            {
                // As the pipe connection should be restored this exception should not be considered as terminating
                _logger.LogWarning(ex, "Pipe connection failed.");
            }
        }
        _logger.LogInformation("Cooperative shutdown service is stopping.");
    }
}