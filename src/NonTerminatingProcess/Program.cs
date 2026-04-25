// Copyright (c) Damian Hickey. All rights reserved.
// See LICENSE in the project root for license information.

using System.Diagnostics;
using LittleForker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NonTerminatingProcess;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;

        if (config.GetValue("debug", false))
        {
            Debugger.Launch();
        }

        var ignoreShutdownSignal = config.GetValue("ignore-shutdown-signal", false);
        var exitWithNonZero = config.GetValue("exit-with-non-zero", false);
        var parentProcessId = config.GetValue<int?>("ParentProcessId");

        if (!ignoreShutdownSignal)
        {
            services.AddCooperativeShutdownHostedService();
        }

        if (parentProcessId.HasValue)
        {
            services.AddWatchParentProcessHostedService(o => o.ParentProcessId = parentProcessId);
        }

        services.AddHostedService<TimeoutHostedService>();

        if (exitWithNonZero)
        {
            services.AddHostedService<NonZeroExitHostedService>();
        }
    })
    .Build();

await host.RunAsync();
