using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Ajs.Concurrency
{
	/// <summary>
	/// Append-only queue of work items to be processed
	/// asynchronously
	/// </summary>
    public sealed class AsyncTaskQueue<T>
	{
		private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
		private readonly Func<CancellationToken, Task> _callback;
		private readonly TimeSpan _interval;
		private readonly CancellationToken _cancellationToken;
		private readonly int _maxWorkers;
		private int _numWorkers;

		public AsyncTaskQueue(Func<Task> callback, int maxWorkers, CancellationToken cancellationToken)
			: this(c => callback(), maxWorkers, TimeSpan.Zero, cancellationToken) { }

		public AsyncTaskQueue(Func<Task> callback, TimeSpan interval, CancellationToken cancellationToken)
			: this(c => callback(), interval, cancellationToken) { }

		public AsyncTaskQueue(Func<CancellationToken, Task> callback, int maxWorkers, CancellationToken cancellationToken)
			: this(callback, maxWorkers, TimeSpan.Zero, cancellationToken) { }

		public AsyncTaskQueue(Func<CancellationToken, Task> callback, TimeSpan interval, CancellationToken cancellationToken)
			: this(callback, 1, interval, cancellationToken) { }

		private AsyncTaskQueue(Func<CancellationToken, Task> callback, int maxWorkers, TimeSpan interval, CancellationToken cancellationToken)
		{
			if (callback == null) throw new ArgumentNullException(nameof(callback));
			if (maxWorkers < 1) throw new ArgumentOutOfRangeException(nameof(maxWorkers), "Must have at least a single degree of parallelism");
			Debug.Assert(maxWorkers == 1 || interval == TimeSpan.Zero, "Parallel tasks must not have an interval");

			_callback = callback;
			_maxWorkers = maxWorkers;
			_interval = interval;
			_cancellationToken = cancellationToken;
		}
		
		/// <summary>
		/// Whether the <see cref="AsyncTaskQueue{t}"/> is active, which
		/// includes the waiting interval between callbacks.
		/// </summary>
		public bool IsActive => _numWorkers > 0;

		/// <summary>
		/// Whether the <see cref="AsyncTaskQueue{T}"/> has been cancelled.
		/// </summary>
		public bool IsCancelled => _cancellationToken.IsCancellationRequested;

		/// <summary>
		/// Event to handle exceptions occuring on the background task.
		/// </summary>
		public event AsyncActionUnhandledExceptionEventHandler UnhandledException;

		/// <summary>
		/// The number of tasks queued but not started.
		/// </summary>
		public int Count => _queue.Count;

		/// <summary>
		/// Add an item to be processed by the <see cref="AsyncTaskQueue{T}"/>.
		/// </summary>
		public void Enqueue(IEnumerable<T> items)
		{
			foreach (var item in items)
			{
				_queue.Enqueue(item);
			}
			Prod();
		}

		public void Enqueue(T item)
		{
			_queue.Enqueue(item);
			Prod();
		}

		private void Prod()
		{
			while (_numWorkers < _maxWorkers && _queue.Count > 0)
			{
				var myTaskNum = Interlocked.Increment(ref _numWorkers) - 1;
				if (myTaskNum < _maxWorkers)
				{
					Task.Run(() => Run(), _cancellationToken)
						.ContinueWith(_ =>
						{
							Interlocked.Decrement(ref _numWorkers);
							Prod();
						}, _cancellationToken);
				}
				else
				{
					Interlocked.Decrement(ref _numWorkers);
				}
			}
		}

		private async Task Run()
		{
			T workItem;
			while (!_cancellationToken.IsCancellationRequested && _queue.TryDequeue(out workItem))
			{
				try
				{
					await _callback(_cancellationToken);
				}
				catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
				{
					// Regular exit - task was cancelled
					continue;
				}
				catch (Exception ex) when (TryHandle(ex))
				{
					// Owner has handled exception
				}

				await Task.Delay(_interval, _cancellationToken);
			}
		}

		private bool TryHandle(Exception exception)
		{
			var eventArgs = new AsyncActionUnhandledExceptionEventArgs(exception);
			UnhandledException?.Invoke(this, eventArgs);
			return eventArgs.Handled;
		}
	}
}
