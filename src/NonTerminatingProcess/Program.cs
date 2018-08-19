using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using LittleForker;
using Serilog;

namespace NonTerminatingProcess
{
    internal sealed class Program
    {
        static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();

            var pid = Process.GetCurrentProcess().Id;
            Log.Logger.Information($"Long running process started. PID={pid}");

            if (args.Contains("--debug"))
            {
                Debugger.Launch();
            }

            try
            {
                using (new ProcessMonitor(ParentExited))
                {
                    using (await CooperativeShutdown.Listen(ExitRequested))
                    {
                        var stopWatch = Stopwatch.StartNew();
                        // Yeah this process is supposed to be "non-terminating"
                        // but we don't want tons of stray instances running
                        // because of tests so it terminates after a long
                        // enough time.
                        while (stopWatch.Elapsed < TimeSpan.FromSeconds(100))
                        {
                            await Task.Delay(100);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, ex.Message);
            }
        }

        private static void ExitRequested()
        {
            Log.Logger.Information("Cooperative shutdown requested.");
            Environment.Exit(0);
        }

        private static void ParentExited(int? processId)
        {
            Log.Logger.Information($"Parent process {processId} exited.");
            Environment.Exit(0);
        }
    }
}
