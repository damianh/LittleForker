using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace LittleForker;

public class WatchParentProcessHostedService : IHostedService
{
    private readonly IHostApplicationLifetime           _applicationLifetime;
    private readonly ILogger<WatchParentProcessHostedService> _logger;
    private readonly WatchParentProcessHostedServiceOptions   _options;

    public WatchParentProcessHostedService(
        IHostApplicationLifetime applicationLifetime,
        ILogger<WatchParentProcessHostedService> logger,
        IOptions<WatchParentProcessHostedServiceOptions> options)
    {
        _applicationLifetime = applicationLifetime;
        _logger              = logger;
        _options             = options.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.ParentProcessId.HasValue)
        {
            _logger.LogWarning("Parent process Id not specified. Process will not be monitoring " +
                               "a parent process and cooperative shutdown is disabled.");
            return Task.CompletedTask;
        }
        _logger.LogInformation("Starting child process with parent process Id {parentProcessId}.", _options.ParentProcessId);

        var process = Process.GetProcesses().SingleOrDefault(pr => pr.Id == _options.ParentProcessId!);
        if (process == null)
        {
            _logger.LogError("Process with Id {parentProcessId} was not found. Exiting.", _options.ParentProcessId);
            _applicationLifetime.StopApplication();
            return Task.CompletedTask;
        }
        
        _logger.LogInformation("Process with Id {parentProcessId} found and will be monitored for exited.", _options.ParentProcessId);
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                _logger.LogInformation("Parent process with Id {processId} exited. Stopping current application.", _options.ParentProcessId);
                _applicationLifetime.StopApplication();
            };
        }
        // Race condition: this may be thrown if the process has already exited before
        // attaching to the Exited event
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation("Process with Id {processId} has already exited. Stopping current application.", _options.ParentProcessId);
            _applicationLifetime.StopApplication();
        }

        if (process.HasExited)
        {
            _logger.LogInformation("Process with Id {processId} has already exited. Stopping current application.", _options.ParentProcessId);
            _applicationLifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}