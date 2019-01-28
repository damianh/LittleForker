using System.Threading;
using System.Threading.Tasks;

namespace LittleForker
{
    public static  class ProcessSupervisorExtensions
    {
        public static Task WhenStateIs(
            this ProcessSupervisor processSupervisor,
            ProcessSupervisor.State processState,
            CancellationToken cancellationToken = default)
        {
            var taskCompletionSource = new TaskCompletionSource<int>();
            cancellationToken.Register(() => taskCompletionSource.TrySetCanceled());

            void Handler(ProcessSupervisor.State state)
            {
                if (processState == state)
                {
                    taskCompletionSource.SetResult(0);
                    processSupervisor.StateChanged -= Handler;
                }
            }

            processSupervisor.StateChanged += Handler;

            return taskCompletionSource.Task;
        }

        public static Task WhenOutputStartsWith(
            this ProcessSupervisor processSupervisor,
            string startsWith,
            CancellationToken cancellationToken = default)
        {
            var taskCompletionSource = new TaskCompletionSource<int>();
            cancellationToken.Register(() => taskCompletionSource.TrySetCanceled());

            void Handler(string data)
            {
                if (data != null && data.StartsWith(startsWith))
                {
                    taskCompletionSource.SetResult(0);
                    processSupervisor.OutputDataReceived -= Handler;
                }
            }

            processSupervisor.OutputDataReceived += Handler;
            return taskCompletionSource.Task;
        }
    }
}