using System.Diagnostics;
using LittleForker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddChildProcessHostedService(context.Configuration);
        services.AddCooperativeShutdownHostedService(context.Configuration);
        services.AddHostedService<TimeoutHostedService>();
    })
    .Build();

var config = host.Services.GetRequiredService<IConfiguration>();
// Running program with --debug=true will attach a debugger.
// Used to assist with debugging LittleForker.
if (config.GetValue("debug", false))
{
    Debugger.Launch();
}

host.Run();