using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LittleForker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

var logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateLogger();
var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(100));
var configRoot = new ConfigurationBuilder()
    .AddCommandLine(args)
    .AddEnvironmentVariables()
    .Build();

// Running program with --debug=true will attach a debugger.
// Used to assist with debugging LittleForker.
if (configRoot.GetValue("debug", false))
{
    Debugger.Launch();
}

var ignoreShutdownSignal = configRoot.GetValue<bool>("ignore-shutdown-signal", false);
if (ignoreShutdownSignal)
{
    Log.Logger.Information("Will ignore Shutdown Signal");
}

var exitWithNonZero = configRoot.GetValue<bool>("exit-with-non-zero", false);
if (exitWithNonZero)
{
    Log.Logger.Information("Will exit with non-zero exit code");
}

var pid = Process.GetCurrentProcess().Id;
Log.Logger.Information($"Long running process started. PID={pid}");

var parentPid = configRoot.GetValue<int?>("ParentProcessId");

using (parentPid.HasValue
    ? new ProcessExitedHelper(parentPid.Value, _ => ParentExited(parentPid.Value), new NullLoggerFactory())
    : NoopDisposable.Instance)
{
    using (await CooperativeShutdown.Listen(ExitRequested, new NullLoggerFactory()))
    {
        // Poll the shutdown token in a tight loop
        while (!shutdown.IsCancellationRequested || ignoreShutdownSignal)
        {
            await Task.Delay(100);
        }
        Log.Information("Exiting.");
    }
}

return exitWithNonZero ? -1 : 0;

void ExitRequested()
{
    Log.Logger.Information("Cooperative shutdown requested.");

    if (ignoreShutdownSignal)
    {
        Log.Logger.Information("Shut down signal ignored.");
        return;
    }

    shutdown.Cancel();
}

void ParentExited(int processId)
{
    Log.Logger.Information($"Parent process {processId} exited.");
    shutdown.Cancel();
}

class NoopDisposable : IDisposable
{
    public void Dispose()
    { }

    internal static readonly IDisposable Instance = new NoopDisposable();
}