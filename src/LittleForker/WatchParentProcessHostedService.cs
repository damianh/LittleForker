// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LittleForker;

/// <summary>
///     A hosted service that watches a parent process and calls
///     <see cref="IHostApplicationLifetime.StopApplication"/> when the parent exits.
/// </summary>
public sealed class WatchParentProcessHostedService : IHostedService, IDisposable
{
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<WatchParentProcessHostedService> _logger;
    private readonly WatchParentProcessHostedServiceOptions _options;
    private ProcessExitedHelper? _helper;

    public WatchParentProcessHostedService(
        IHostApplicationLifetime applicationLifetime,
        IOptions<WatchParentProcessHostedServiceOptions> options,
        ILogger<WatchParentProcessHostedService> logger)
    {
        _applicationLifetime = applicationLifetime;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.ParentProcessId is not { } pid)
        {
            _logger.LogInformation("No parent process ID configured; parent watching is disabled.");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Watching parent process {ParentProcessId}.", pid);
        _helper = new ProcessExitedHelper(pid, OnParentExited, new LoggerFactoryAdapter(_logger));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _helper?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _helper?.Dispose();

    private void OnParentExited(ProcessExitedHelper helper)
    {
        _logger.LogInformation("Parent process {ParentProcessId} exited. Stopping application.", helper.ProcessId);
        _applicationLifetime.StopApplication();
    }

    /// <summary>Minimal <see cref="ILoggerFactory"/> adapter so <see cref="ProcessExitedHelper"/> can use our logger.</summary>
    private sealed class LoggerFactoryAdapter : ILoggerFactory
    {
        private readonly ILogger _logger;

        internal LoggerFactoryAdapter(ILogger logger) => _logger = logger;

        public ILogger CreateLogger(string categoryName) => _logger;

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }
    }
}
