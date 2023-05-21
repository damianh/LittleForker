using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

public class TimeoutHostedService : BackgroundService
{
    private readonly IHostApplicationLifetime _applicationLifetime;

    public TimeoutHostedService(IHostApplicationLifetime applicationLifetime)
    {
        _applicationLifetime = applicationLifetime;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(100), stoppingToken);
        _applicationLifetime.StopApplication();
    }
}