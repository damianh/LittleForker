using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LittleForker;

public class ChildProcessHostedService : IHostedService
{
    private readonly IConfiguration                     _configuration;
    private readonly IHostApplicationLifetime           _hostApplicationLifetime;
    private readonly ILogger<ChildProcessHostedService> _logger;
    private readonly ChildProcessConfig                 _config;

    public ChildProcessHostedService(
        IConfiguration configurationRoot,
        IHostApplicationLifetime hostApplicationLifetime,
        ILogger<ChildProcessHostedService> logger)
    {
        _configuration           = configurationRoot;
        _hostApplicationLifetime = hostApplicationLifetime;
        _logger                  = logger;
        _config                  = new ChildProcessConfig();
        _configuration.Bind(_config);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_config.ParentProcessId))
        {
            _logger.LogWarning("Parent process Id not specified. Child process will not be monitoring " +
                                   "a parent process and cooperative shutdown is not enabled.");
            return Task.CompletedTask;
        }
        _logger.LogInformation($"Starting child process with parent process Id {_config.ParentProcessId}.");


        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken  cancellationToken)
    {
        _logger.LogInformation($"Stopping child process with parent process Id {_config.ParentProcessId}.");
        return Task.CompletedTask;
    }

    private class ChildProcessConfig
    {
        public string? ParentProcessId { get; set; } = null!;
    }
}