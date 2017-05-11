using System.Threading;
using System.Threading.Tasks;

namespace ConcurrentPatterns
{
    // Inspired by https://blogs.msdn.microsoft.com/pfxteam/2012/02/11/building-async-coordination-primitives-part-1-asyncmanualresetevent/

    /// <summary>
    /// Represent a gate which can be opened or closed by the caller.
    /// </summary>
    public class AsyncManualResetEvent
    {
        private TaskCompletionSource<bool> _taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public AsyncManualResetEvent() : this(false) { }
        public AsyncManualResetEvent(bool isSignalled)
        {
            if (isSignalled)
            {
                _taskCompletionSource.TrySetResult(true);
            }
        }

        /// <summary>
        /// Returns a task which will be completed when the gate is opened.
        /// </summary>
        public Task WaitAsync() => _taskCompletionSource.Task;

        /// <summary>
        /// Opens the gate.
        /// </summary>
        public void Set() => _taskCompletionSource.TrySetResult(true);

        /// <summary>
        /// Closes the gate.
        /// </summary>
        public void Reset()
        {
            while (true)
            {
                var tcs = _taskCompletionSource;
                if (!tcs.Task.IsCompleted)
                {
                    break;
                }

                Interlocked.CompareExchange(ref _taskCompletionSource, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously), tcs);
                // If we didn't swap it means another thread has also just reset the event.
            }
        }

    }
}
