﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ajs.Concurrency.BackgroundActions
{
    /// <summary>
    /// Task-based approach to periodically running a
    /// callback with a fixed interval between runs.
    /// </summary>
    /// <remarks>
    /// Differs from the Timer concept because the
    /// interval between callbacks begins ticking
    /// when the last callback ended.
    /// </remarks>
    public sealed class Poller
    {

        private readonly Func<CancellationToken, Task> _callback;
        private readonly CancellationToken _cancellationToken;
        private readonly DelayTaskSource _delayTaskSource;
        private int _hasStartBeenCalled;

        #region Construction

        public Poller(string name, Func<Task> callback, TimeSpan interval, CancellationToken cancellationToken)
            : this(name, t => callback(), interval, cancellationToken) { }

        public Poller(string name, Func<CancellationToken, Task> callback, TimeSpan interval, CancellationToken cancellationToken)
        {
            if (interval < TimeSpan.Zero) throw new InvalidOperationException("interval must not be a negative duration");

            Name = name;
            Interval = interval;
            _callback = callback;
            _cancellationToken = cancellationToken;
            _delayTaskSource = new DelayTaskSource(_cancellationToken);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Name of the <see cref="Poller"/>
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The duration between the end of one run
        /// and the start of the next.
        /// </summary>
        public TimeSpan Interval { get; }

        /// <summary>
        /// Whether the <see cref="Poller"/> has already been started.
        /// This occurs after any initial delay period.
        /// </summary>
        public bool IsStarted { get; private set; }

        /// <summary>
        /// Whether the <see cref="Poller"/> is active, which
        /// includes the waiting interval between callbacks.
        /// </summary>
        public bool IsActive => IsStarted && !IsCompleted;

        /// <summary>
        /// Whether the callback is currently executing.
        /// </summary>
        public bool IsBusy { get; private set; }

        /// <summary>
        /// Whether the <see cref="Poller"/> has completed and is
        /// no longer active.
        /// </summary>
        public bool IsCompleted { get; private set; }

        /// <summary>
        /// Whether the <see cref="Poller"/> has been cancelled.
        /// </summary>
        public bool IsCancelled => _cancellationToken.IsCancellationRequested;

        /// <summary>
        /// Event to handle exceptions occuring on the background task.
        /// </summary>
        public event BackgroundActionUnhandledExceptionEventHandler UnhandledException;

        #endregion

        #region Controlling

        /// <summary>
        /// Immediately stop waiting an interval.
        /// If the <see cref="Poller"/> is not between callbacks, nothing happens.
        /// </summary>
        public void Wake()
        {
            if (!IsStarted) throw new InvalidOperationException("Poller has not yet been started");
            _delayTaskSource.Cancel();
        }

        /// <summary>
        /// Start the <see cref="Poller"/> after <paramref name="initialDelay"/>.
        /// </summary>
        public void Start(TimeSpan initialDelay)
        {
            if (initialDelay < TimeSpan.Zero) throw new InvalidOperationException("Initial Delay must not be a negative duration");

            var wasStarted = 1 == Interlocked.Exchange(ref _hasStartBeenCalled, 1);
            if (wasStarted) throw new InvalidOperationException("Poller has already been started");

            Task.Delay(initialDelay, _cancellationToken).ContinueWith(t => StartInternal(), _cancellationToken);
        }

        /// <summary>
        /// Start the poller immediately.
        /// </summary>
        public void Start()
        {
            var wasStarted = 1 == Interlocked.Exchange(ref _hasStartBeenCalled, 1);
            if (wasStarted) throw new InvalidOperationException("Poller has already been started");
            StartInternal();
        }

        #endregion

        #region The Works

        private void StartInternal() => Task.Run(() => Run(), _cancellationToken);

        private async Task Run()
        {
            try
            {
                IsStarted = true;

                while (!_cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        IsBusy = true;
                        await _callback(_cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Regular exit - task was cancelled
                    }
                    catch (Exception ex) when (TryHandle(ex))
                    {
                        // Owner has handled exception
                    }
                    finally
                    {
                        IsBusy = false;
                    }

                    await RunIntervalAsync();
                }
            }
            finally
            {
                IsCompleted = true;
            }
        }

        private async Task RunIntervalAsync()
        {
            try
            {
                await _delayTaskSource.Delay(Interval);
            }
            catch (OperationCanceledException)
            {
                // Do nothing - waiting was cancelled
            }
        }

        private bool TryHandle(Exception exception)
        {
            var eventArgs = new BackgroundActionUnhandledExceptionEventArgs(exception);
            UnhandledException?.Invoke(this, eventArgs);
            return eventArgs.Handled;
        }

        #endregion

        #region Static Construction

        /// <summary>
        /// Get a new <see cref="Poller"/> which will start immediately.
        /// </summary>
        public static Poller StartNew(string name, Func<Task> callback, TimeSpan interval, CancellationToken cancellationToken)
        {
            var result = new Poller(name, callback, interval, cancellationToken);
            result.Start();
            return result;
        }

        /// <summary>
        /// Get a new <see cref="Poller"/> which will start immediately.
        /// </summary>
        public static Poller StartNew(string name, Func<CancellationToken, Task> callback, TimeSpan interval, CancellationToken cancellationToken)
        {
            var result = new Poller(name, callback, interval, cancellationToken);
            result.Start();
            return result;
        }

        /// <summary>
        /// Get a new <see cref="Poller"/> which will start after a <paramref name="initialDelay"/>.
        /// </summary>
        public static Poller StartDelayed(string name, Func<Task> callback, TimeSpan interval, TimeSpan initialDelay, CancellationToken cancellationToken)
        {
            var result = new Poller(name, callback, interval, cancellationToken);
            result.Start(initialDelay);
            return result;
        }

        /// <summary>
        /// Get a new <see cref="Poller"/> which will start after a <paramref name="initialDelay"/>.
        /// </summary>
        public static Poller StartDelayed(string name, Func<CancellationToken, Task> callback, TimeSpan interval, TimeSpan initialDelay, CancellationToken cancellationToken)
        {
            var result = new Poller(name, callback, interval, cancellationToken);
            result.Start(initialDelay);
            return result;
        }

        #endregion
    }
}
