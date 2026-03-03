using System.Threading;
using System.Threading.Tasks;

namespace LittleForker;

public static class ProcessSupervisorExtensions
{
    public static Task WhenStateIs(
        this ProcessSupervisor  processSupervisor,
        ProcessSupervisor.State processState,
        CancellationToken       cancellationToken = default)
    {
        var taskCompletionSource = new TaskCompletionSource<int>();
        var registration = cancellationToken.Register(() => taskCompletionSource.TrySetCanceled());

        // Check current state first.
        if (processSupervisor.CurrentState == processState)
        {
            taskCompletionSource.TrySetResult(0);
            registration.Dispose();
            return taskCompletionSource.Task;
        }

        void Handler(ProcessSupervisor.State state)
        {
            if (processState == state)
            {
                taskCompletionSource.TrySetResult(0);
                processSupervisor.StateChanged -= Handler;
                registration.Dispose();
            }
        }

        processSupervisor.StateChanged += Handler;

        // Double-check after subscribing (race: state changed between check and subscribe).
        if (processSupervisor.CurrentState == processState)
        {
            taskCompletionSource.TrySetResult(0);
            processSupervisor.StateChanged -= Handler;
            registration.Dispose();
        }

        return taskCompletionSource.Task;
    }

    public static Task WhenOutputStartsWith(
        this ProcessSupervisor processSupervisor,
        string                 startsWith,
        CancellationToken      cancellationToken = default)
    {
        var taskCompletionSource = new TaskCompletionSource<int>();
        var registration = cancellationToken.Register(() => taskCompletionSource.TrySetCanceled());

        void Handler(string data)
        {
            if (data != null && data.StartsWith(startsWith))
            {
                taskCompletionSource.TrySetResult(0);
                processSupervisor.OutputDataReceived -= Handler;
                registration.Dispose();
            }
        }

        processSupervisor.OutputDataReceived += Handler;
        return taskCompletionSource.Task;
    }
}