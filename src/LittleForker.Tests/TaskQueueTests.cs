using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace LittleForker;

public sealed class TaskQueueTests
{
    [Fact]
    public async Task EnqueueProcessesTasksSequentially()
    {
        using var queue = new TaskQueue();
        var executing = 0;
        var maxConcurrent = 0;
        var tasks = new Task[10];

        for (var i = 0; i < tasks.Length; i++)
        {
            tasks[i] = queue.Enqueue(async _ =>
            {
                var current = Interlocked.Increment(ref executing);
                // Track the maximum observed concurrency.
                InterlockedMax(ref maxConcurrent, current);
                await Task.Delay(10);
                Interlocked.Decrement(ref executing);
            });
        }

        await Task.WhenAll(tasks);
        maxConcurrent.ShouldBe(1, "Tasks should execute one at a time");
    }

    [Fact]
    public async Task EnqueueUnderConcurrentLoadNoTasksDropped()
    {
        using var queue = new TaskQueue();
        const int taskCount = 200;
        var completed = new ConcurrentBag<int>();
        var tasks = new Task[taskCount];

        // Enqueue from multiple threads simultaneously.
        Parallel.For(0, taskCount, i =>
        {
            tasks[i] = queue.Enqueue(() =>
            {
                completed.Add(i);
            });
        });

        await Task.WhenAll(tasks);
        completed.Count.ShouldBe(taskCount, "All enqueued tasks should complete");
    }

    [Fact]
    public async Task DisposeDoesNotBlockAndCancelsPendingTasks()
    {
        var queue = new TaskQueue();
        var gate = new TaskCompletionSource();

        // Enqueue a blocking task so the second task stays pending.
        var first = queue.Enqueue(async _ =>
        {
            await gate.Task;
        });

        // This task should be pending in the queue.
        var second = queue.Enqueue(_ => Task.CompletedTask);

        queue.Dispose();
        gate.TrySetResult(); // Unblock first so it can resolve.

        // After dispose, pending tasks should be canceled.
        await Should.ThrowAsync<TaskCanceledException>(async () => await second);
    }

    [Fact]
    public async Task EnqueueAfterDisposeReturnsCanceledTask()
    {
        var queue = new TaskQueue();
        queue.Dispose();

        var task = queue.Enqueue(() => { });
        await Should.ThrowAsync<TaskCanceledException>(async () => await task);
    }

    private static void InterlockedMax(ref int location, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref location);
            if (value <= current)
            {
                return;
            }
        } while (Interlocked.CompareExchange(ref location, value, current) != current);
    }
}
