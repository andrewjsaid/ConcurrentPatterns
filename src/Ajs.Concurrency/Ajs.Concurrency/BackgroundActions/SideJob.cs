using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Ajs.Concurrency.BackgroundActions
{
    /// <summary>
    /// Represents a callback which will run unless actively delayed or executed immediately.
    /// </summary>
    public sealed class SideJob
    {
        private const long IsRunning = long.MaxValue;
        private const long IsRunningWithReschedule = long.MaxValue - 1;
        private const long RunImmediately = long.MaxValue - 2;

        private readonly Func<CancellationToken, Task> _callback;
        private readonly CancellationToken _cancellationToken;
        private readonly DelayTaskSource _delayTaskSource;
        private long _nextRunTicks;

        #region Construction

        public SideJob(string name, Func<Task> callback, TimeSpan interval, CancellationToken cancellationToken)
            : this(name, t => callback(), interval, cancellationToken)
        {
        }

        public SideJob(string name, Func<CancellationToken, Task> callback, TimeSpan interval, CancellationToken cancellationToken)
        {
            if (interval < TimeSpan.Zero) throw new InvalidOperationException("interval must not be a negative duration");

            Name = name;
            Interval = interval;
            _callback = callback;
            _cancellationToken = cancellationToken;
            _delayTaskSource = new DelayTaskSource(cancellationToken);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Name of the <see cref="SideJob"/>
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The waiting duration between the last notification
        /// received to the background task being invoked.
        /// </summary>
        public TimeSpan Interval { get; }

        /// <summary>
        /// Whether the callback is currently executing.
        /// </summary>
        public bool IsBusy { get; private set; }

        /// <summary>
        /// Whether the <see cref="SideJob"/> has completed and is
        /// no longer active.
        /// </summary>
        public bool IsCompleted => IsCancelled && !IsBusy;

        /// <summary>
        /// Whether the <see cref="SideJob"/> has been cancelled.
        /// </summary>
        public bool IsCancelled => _cancellationToken.IsCancellationRequested;

        /// <summary>
        /// Event to handle exceptions occuring on the background task.
        /// </summary>
        public event BackgroundActionUnhandledExceptionEventHandler UnhandledException;

        #endregion

        #region Controlling

        /// <summary>
        /// Immediately runs the callback on a background thread.
        /// If the callback is already running, it is scheduled with
        /// the regular delay.
        /// </summary>
        public void Wake()
        {
            if (_cancellationToken.IsCancellationRequested)
                return;

            var assumedValue = Interlocked.Read(ref _nextRunTicks);

            while (true)
            {
                long calculatedTicks;
                if (assumedValue == IsRunning)
                {
                    // Tell the task to reschedule when done
                    calculatedTicks = IsRunningWithReschedule;
                }
                else if (assumedValue == IsRunningWithReschedule || assumedValue == RunImmediately)
                {
                    // Nothing to do
                    return;
                }
                else
                {
                    // Run immediately
                    calculatedTicks = RunImmediately;
                }

                // Attempt to set to new value
                var actualValue = Interlocked.CompareExchange(ref _nextRunTicks, calculatedTicks, assumedValue);

                if (actualValue == assumedValue)
                {
                    if (actualValue == 0)
                    {
                        // Not scheduled - run immediately
                        Task.Run(() => Run(), _cancellationToken);
                    }
                    else if (actualValue != IsRunning)
                    {
                        // It is waiting - wake it up
                        _delayTaskSource.Cancel();
                    }
                    return;
                }

                // Else, re-do the calculations
                assumedValue = actualValue;
            }
        }

        /// <summary>
        /// Start the callback after waiting
        /// <see cref="Interval"/>, pushing back
        /// any previous notifications.
        /// </summary>
        public void Delay() => Delay(Interval);

        /// <summary>
        /// Start the callback after waiting
        /// <paramref name="delay"/>, pushing back
        /// any previous notifications.
        /// </summary>
        public void Delay(TimeSpan delay)
        {
            if (delay < TimeSpan.Zero) throw new InvalidOperationException("Delay must not be a negative duration");

            if (_cancellationToken.IsCancellationRequested)
                return;

            var myNextRunTicks = DateTime.UtcNow.Add(Interval).Ticks;
            var assumedValue = Interlocked.Read(ref _nextRunTicks);

            while (true)
            {
                long calculatedTicks;
                if (assumedValue == IsRunning)
                {
                    // Tell the task to reschedule when done
                    calculatedTicks = IsRunningWithReschedule;
                }
                else if (assumedValue == RunImmediately)
                {
                    // The task is to be run immediately without delay
                    return;
                }
                else if (assumedValue > myNextRunTicks)
                {
                    // The current schedule is already further
                    return;
                }
                else
                {
                    calculatedTicks = myNextRunTicks;
                }
                
                // Attempt to set to new value
                var actualValue = Interlocked.CompareExchange(ref _nextRunTicks, calculatedTicks, assumedValue);

                if (actualValue == assumedValue)
                {
                    if (actualValue != IsRunning)
                    {
                        // If it is already running we can assume that it will schedule the task when finished.
                        // But since it is not, we schedule it.
                        ScheduleTask();
                    }
                    return;
                }

                // Else, re-do the calculations
                assumedValue = actualValue;
            }
        }

        #endregion

        #region The Works

        private void ScheduleTask()
        {
            var runAt = Interlocked.Read(ref _nextRunTicks);
            var waitDuration = TimeSpan.FromTicks(runAt - DateTime.UtcNow.Ticks);

            if (waitDuration <= TimeSpan.Zero)
            {
                // Run immediately
                Task.Run(() => Run(), _cancellationToken);
            }
            else
            {
                // Otherwise run after a delay
                _delayTaskSource.Delay(waitDuration)
                                .ContinueWith(_ => Run(), _cancellationToken);
            }
        }

        private async Task Run()
        {
            if (!TryEnterRunState())
                return; // Could not enter run state

            try
            {
                IsBusy = true;

                // Check cancellation after setting IsBusy so that
                // IsCompleted never returns True when work is still
                // yet to be done, which would occur in the few 
                // instructions between this and setting IsBusy to True.
                if (_cancellationToken.IsCancellationRequested)
                    return;

                await _callback(_cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Do nothing - task was cancelled
            }
            catch (Exception ex) when (TryHandle(ex))
            {
                // Owner has handled exception
            }
            finally
            {
                IsBusy = false;

                ExitRunState();
            }
        }

        private bool TryEnterRunState()
        {
            // Delay has finished - check if it is correct
            var assumedScheduleTicks = Interlocked.Read(ref _nextRunTicks);

            while (true)
            {
                if (assumedScheduleTicks == IsRunning || assumedScheduleTicks == IsRunningWithReschedule)
                {
                    // Warning: this should not be the case - do not run
                    Debug.Fail($"Found {nameof(_nextRunTicks)} to be in unexpected Running state {assumedScheduleTicks}");
                    return false;
                }

                if (assumedScheduleTicks != RunImmediately && assumedScheduleTicks > DateTime.UtcNow.Ticks)
                {
                    // Schedule the task again and don't run
                    ScheduleTask();
                    return false;
                }

                // Set field to magic number signifying we are running
                var scheduledTicks = Interlocked.CompareExchange(ref _nextRunTicks, IsRunning, assumedScheduleTicks);
                if (scheduledTicks == assumedScheduleTicks)
                {
                    // Run the task
                    return true;
                }

                // Else, re-do the calculations
                assumedScheduleTicks = scheduledTicks;
            }
        }

        private void ExitRunState()
        {
            // Loop is required in case Reschedule is being set at the same time.
            var assumed = Interlocked.Read(ref _nextRunTicks);
            while (true)
            {
                if (assumed != IsRunning && assumed != IsRunningWithReschedule)
                {
                    Debug.Fail($"Unexpected value for {nameof(_nextRunTicks)} in finished task: {assumed}");
                    return;
                }

                var actual = Interlocked.CompareExchange(ref _nextRunTicks, 0, assumed);
                if (actual == assumed)
                {
                    if (actual == IsRunningWithReschedule)
                    {
                        // Prod in the name of the one who asked for the Reschedule
                        Delay();
                    }

                    // Exit this task safely after having resheduled
                    return;
                }

                // Else, re-do the calculations
                assumed = actual;
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
        /// Get a new <see cref="SideJob"/> which will start after the specified delay.
        /// </summary>
        public static SideJob StartNew(string name, Func<Task> callback, TimeSpan interval, CancellationToken cancellationToken)
        {
            var result = new SideJob(name, callback, interval, cancellationToken);
            result.Delay();
            return result;
        }

        /// <summary>
        /// Get a new <see cref="SideJob"/> which will start after the specified delay.
        /// </summary>
        public static SideJob StartNew(string name, Func<CancellationToken, Task> callback, TimeSpan interval, CancellationToken cancellationToken)
        {
            var result = new SideJob(name, callback, interval, cancellationToken);
            result.Delay();
            return result;
        }

        /// <summary>
        /// Get a new <see cref="SideJob"/> which will start after <paramref name="initialDelay"/>.
        /// </summary>
        public static SideJob StartNew(string name, Func<Task> callback, TimeSpan interval, TimeSpan initialDelay, CancellationToken cancellationToken)
        {
            var result = new SideJob(name, callback, interval, cancellationToken);
            result.Delay(initialDelay);
            return result;
        }

        /// <summary>
        /// Get a new <see cref="SideJob"/> which will start after <paramref name="initialDelay"/>.
        /// </summary>
        public static SideJob StartNew(string name, Func<CancellationToken, Task> callback, TimeSpan interval, TimeSpan initialDelay, CancellationToken cancellationToken)
        {
            var result = new SideJob(name, callback, interval, cancellationToken);
            result.Delay(initialDelay);
            return result;
        }

        #endregion
    }
}
