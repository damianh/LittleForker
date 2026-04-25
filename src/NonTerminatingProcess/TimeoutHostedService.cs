// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

using Microsoft.Extensions.Hosting;

namespace NonTerminatingProcess;

/// <summary>Safety timeout — stops the process after 100 seconds to prevent hanging in tests.</summary>
internal sealed class TimeoutHostedService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) =>
        await Task.Delay(TimeSpan.FromSeconds(100), stoppingToken);
}
