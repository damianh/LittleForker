// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

using Microsoft.Extensions.Hosting;

namespace NonTerminatingProcess;

/// <summary>Sets a non-zero exit code when the host is stopping.</summary>
internal sealed class NonZeroExitHostedService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Environment.ExitCode = -1;
        return Task.CompletedTask;
    }
}
