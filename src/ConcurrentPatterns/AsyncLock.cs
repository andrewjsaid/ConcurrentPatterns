using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ConcurrentPatterns
{
    // Inspired by https://blogs.msdn.microsoft.com/pfxteam/2012/02/12/building-async-coordination-primitives-part-6-asynclock/

    /// <summary>
    /// Represents a lock which can be waited for asynchronously. 
    /// </summary>
    public class AsyncLock
    {
        private readonly object _lock = new object();
        private readonly Queue<TaskCompletionSource<IDisposable>> _taskCompletionSources = new Queue<TaskCompletionSource<IDisposable>>();
        private readonly Task<IDisposable> _completedTask;
        private bool _isSignalled = true;

        public AsyncLock()
        {
            _completedTask = Task.FromResult<IDisposable>(new Releaser(this));
        }

        public Task<IDisposable> WaitAsync()
        {
            lock (_lock)
            {
                if (_isSignalled)
                {
                    _isSignalled = false;
                    return _completedTask;
                }
                else
                {
                    var tcs = new TaskCompletionSource<IDisposable>(this, TaskCreationOptions.RunContinuationsAsynchronously);
                    _taskCompletionSources.Enqueue(tcs);
                    return tcs.Task;
                }
            }
        }

        private void ReleaseLock()
        {
            TaskCompletionSource<IDisposable> tcs = null;
            lock (_lock)
            {
                if (_taskCompletionSources.Count > 0)
                {
                    tcs = _taskCompletionSources.Dequeue();
                }
                else if (!_isSignalled)
                {
                    _isSignalled = true;
                }
            }
            tcs?.TrySetResult(new Releaser(this));
        }

        private class Releaser : IDisposable
        {
            private readonly AsyncLock _asyncLock;

            internal Releaser(AsyncLock asyncLock)
            {
                _asyncLock = asyncLock;
            }

            public void Dispose() => _asyncLock.ReleaseLock();
        }
    }
}
