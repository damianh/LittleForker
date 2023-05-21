using System;
using System.Threading;
using System.Threading.Tasks;

namespace LittleForker;

internal static class TaskExtensions
{
    // https://stackoverflow.com/a/28626769

    internal static Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        return task.IsCompleted // fast-path optimization
            ? task
            : task.ContinueWith(
                completedTask => completedTask.GetAwaiter().GetResult(),
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    internal static async Task TimeoutAfter(this Task task, TimeSpan timeout)
    {
        using (var timeoutCancellationTokenSource = new CancellationTokenSource())
        {
            var completedTask = await Task
                .WhenAny(task, Task.Delay(timeout, timeoutCancellationTokenSource.Token))
                .ConfigureAwait(false);

            if (completedTask == task)
            {
                timeoutCancellationTokenSource.Cancel();
                await task.ConfigureAwait(false);
                return;
            }

            throw new TimeoutException("The operation has timed out.");
        }
    }

    internal static async Task<T> TimeoutAfter<T>(this Task<T> task, TimeSpan timeout)
    {
        using (var timeoutCancellationTokenSource = new CancellationTokenSource())
        {
            var completedTask = await Task
                .WhenAny(task, Task.Delay(timeout, timeoutCancellationTokenSource.Token))
                .ConfigureAwait(false);
            if (completedTask == task)
            {
                timeoutCancellationTokenSource.Cancel();
                return await task.ConfigureAwait(false);
            }

            throw new TimeoutException("The operation has timed out.");
        }
    }
}