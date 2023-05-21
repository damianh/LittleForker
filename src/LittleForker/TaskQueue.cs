using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace LittleForker;

/// <summary>
///     Represents a queue of tasks where a task is processed one at a time. When disposed
///     the outstanding tasks are cancelled.
/// </summary>
internal class TaskQueue : IDisposable
{
    private readonly ConcurrentQueue<Func<Task>> _taskQueue    = new();
    private readonly CancellationTokenSource     _isDisposed   = new();
    private readonly InterlockedBoolean          _isProcessing = new();

    /// <summary>
    ///     Enqueues a task for processing.
    /// </summary>
    /// <param name="action">The operations to invoke.</param>
    /// <returns>A task representing the operation. Awaiting is optional.</returns>
    public Task Enqueue(Action action)
    {
        var task = Enqueue(_ =>
        {
            action();
            return Task.CompletedTask;
        });
        return task;
    }

    /// <summary>
    ///     Enqueues a task for processing.
    /// </summary>
    /// <param name="function">The operations to invoke.</param>
    /// <returns>A task representing the operation. Awaiting is optional.</returns>
    public Task<T> Enqueue<T>(Func<T> function)
    {
        var task = Enqueue(_ =>
        {
            var result = function();
            return Task.FromResult(result);
        });
        return task;
    }

    /// <summary>
    ///     Enqueues a task for processing.
    /// </summary>
    /// <param name="function">The operation to invoke that is cooperatively cancelable.</param>
    /// <returns>A task representing the operation. Awaiting is optional.</returns>
    public Task Enqueue(Func<CancellationToken, Task> function)
    {
        var task = Enqueue(async ct =>
        {
            await function(ct).ConfigureAwait(false);
            return true;
        });
        return task;
    }

    /// <summary>
    ///     Enqueues a task for processing.
    /// </summary>
    /// <param name="function">The operation to invoke that is cooperatively  cancelable.</param>
    /// <returns>A task representing the operation. Awaiting is optional.</returns>
    public Task<TResult> Enqueue<TResult>(Func<CancellationToken, Task<TResult>> function)
    {
        return EnqueueInternal(_taskQueue, function);
    }

    private Task<TResult> EnqueueInternal<TResult>(
        ConcurrentQueue<Func<Task>>            taskQueue,
        Func<CancellationToken, Task<TResult>> function)
    {
        var tcs = new TaskCompletionSource<TResult>();
        if (_isDisposed.IsCancellationRequested)
        {
            tcs.SetCanceled();
            return tcs.Task;
        }
        taskQueue.Enqueue(async () =>
        {
            if (_isDisposed.IsCancellationRequested)
            {
                tcs.SetCanceled();
                return;
            }
            try
            {
                var result = await function(_isDisposed.Token)
                    .ConfigureAwait(false);

                tcs.SetResult(result);
            }
            catch (OperationCanceledException)
            {
                tcs.SetCanceled();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }

        });
        if (_isProcessing.CompareExchange(true, false) == false)
        {
            Task.Run(ProcessTaskQueue).ConfigureAwait(false);
        }
        return tcs.Task;
    }

    private async Task ProcessTaskQueue()
    {
        do
        {
            if (_taskQueue.TryDequeue(out Func<Task> function))
            {
                await function().ConfigureAwait(false);
            }
            _isProcessing.Set(false);
        } while (_taskQueue.Count > 0 && _isProcessing.CompareExchange(true, false) == false);
    }

    public void Dispose()
    {
        _isDisposed.Cancel();
    }
}