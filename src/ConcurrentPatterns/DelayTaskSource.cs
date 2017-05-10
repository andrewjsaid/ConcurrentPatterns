using System;
using System.Threading;
using System.Threading.Tasks;

namespace ConcurrentPatterns
{
    /// <summary>
    /// Creates Delayed Tasks which can be cancelled.
    /// </summary>
    public sealed class DelayTaskSource
    {
        private readonly CancellationToken _parentCancellationToken;

        // It is okay not to call dispose on this, since in no code path will the WaitHandle be created.
        private CancellationTokenSource _cancellationTokenSource;

        public DelayTaskSource(CancellationToken cancellationToken)
        {
            _parentCancellationToken = cancellationToken;
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(_parentCancellationToken);
        }

		/// <summary>
		/// Wait a specified amount of time to pass or for
		/// <see cref="Cancel"/> to be called,
		/// </summary>
        public async Task Delay(TimeSpan delay)
        {
            try
            {
                await Task.Delay(delay, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException) when (!_parentCancellationToken.IsCancellationRequested)
            {
                // We were cancelled, which is an implementation detail to our caller.
            }
        }

		/// <summary>
		/// Stops all delays currently waiting and immediately executes
		/// the continuations.
		/// </summary>
        public void Cancel()
        {
            if (_parentCancellationToken.IsCancellationRequested)
                return; // Will make no difference

            var assumed = _cancellationTokenSource;
            var newValue = CancellationTokenSource.CreateLinkedTokenSource(_parentCancellationToken);
            var actual = Interlocked.CompareExchange(ref _cancellationTokenSource, newValue, assumed);
            if (assumed == actual)
            {
                // This thread was the one to replace actual / assumed, we are the last to have a reference to it.
                actual.Cancel();
            }
            // else the task was cancelled so closely to this one it makes no difference.
        }

    }
}
