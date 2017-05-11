using System.Collections.Generic;
using System.Threading.Tasks;

namespace ConcurrentPatterns
{
    // Inspired by https://blogs.msdn.microsoft.com/pfxteam/2012/02/11/building-async-coordination-primitives-part-2-asyncautoresetevent/

    /// <summary>
    /// Represents a wait event which can be signalled to allow a single
    /// waiting task to pass through before moving back to the non-signalled state.
    /// </summary>
    public class AsyncAutoResetEvent
    {
        private readonly object _lock = new object();
        private readonly Queue<TaskCompletionSource<bool>> _taskCompletionSources = new Queue<TaskCompletionSource<bool>>();
        private bool _isSignalled;

        public AsyncAutoResetEvent() : this(false) { }
        public AsyncAutoResetEvent(bool isSignalled)
        {
            _isSignalled = isSignalled;
        }

        /// <summary>
        /// Returns a task which will be completed when the gate is opened.
        /// </summary>
        public Task WaitAsync()
        {
            lock (_lock)
            {
                if (_isSignalled)
                {
                    _isSignalled = false;
                    return Task.CompletedTask;
                }
                else
                {
                    var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _taskCompletionSources.Enqueue(tcs);
                    return tcs.Task;
                }

            }
        }

        /// <summary>
        /// Opens the gate, allowing a single task through before blocking again.
        /// </summary>
        public void Set()
        {
            TaskCompletionSource<bool> tcs = null;
            lock (_lock)
            {
                if (_taskCompletionSources.Count > 0)
                {
                    tcs = _taskCompletionSources.Dequeue();
                }
                else if(!_isSignalled)
                {
                    _isSignalled = true;
                }
            }
            tcs?.TrySetResult(true);
        }
    }
}
