using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LittleForker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

namespace NonTerminatingProcess
{
    internal sealed class Program
    {
        // Yeah this process is supposed to be "non-terminating"
        // but we don't want tons of orphaned instances running
        // because of tests so it terminates after a long
        // enough time (100 seconds)
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(100));
        private readonly IConfigurationRoot _configRoot;
        private readonly bool _ignoreShutdownSignal;

        static Program()
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();
        }

        private Program(string[] args)
        {
            _configRoot = new ConfigurationBuilder()
                .AddCommandLine(args)
                .AddEnvironmentVariables()
                .Build();

            // Running program with --debug=true will attach a debugger.
            // Used to assist with debugging LittleForker.
            if (_configRoot.GetValue("debug", false)) 
            {
                Debugger.Launch();
            }
            
            _ignoreShutdownSignal = _configRoot.GetValue<bool>("ignore-shutdown-signal", false);
            if (_ignoreShutdownSignal)
            {
                Log.Logger.Information("Ignoring Shutdown Signal");
            }
        }

        private async Task Run()
        {
            var pid = Process.GetCurrentProcess().Id;
            Log.Logger.Information($"Long running process started. PID={pid}");

            var parentPid = _configRoot.GetValue<int?>("ParentProcessId");

            using (parentPid.HasValue
                ? new ProcessExitedHelper(parentPid.Value, _ => ParentExited(parentPid.Value), new NullLoggerFactory())
                : NoopDisposable.Instance)
            {
                using (await CooperativeShutdown.Listen(ExitRequested, new NullLoggerFactory()))
                {
                    // Poll the shutdown token in a tight loop
                    while(!_shutdown.IsCancellationRequested || _ignoreShutdownSignal)
                    {
                        await Task.Delay(100);
                    }
                    Log.Information("Exiting.");
                }
            }
        }

        static Task Main(string[] args) => new Program(args).Run();

        private void ExitRequested()
        {
            Log.Logger.Information("Cooperative shutdown requested.");
            _shutdown.Cancel();
        }

        private void ParentExited(int processId)
        {
            Log.Logger.Information($"Parent process {processId} exited.");
            _shutdown.Cancel();
        }

        private class NoopDisposable : IDisposable
        {
            public void Dispose()
            {}

            internal static readonly IDisposable Instance = new NoopDisposable();
        }
    }
}
