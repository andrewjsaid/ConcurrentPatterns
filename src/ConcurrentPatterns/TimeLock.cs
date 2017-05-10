using System;
using System.Threading;

namespace ConcurrentPatterns
{
	/// <summary>
	/// Controls a resource for a limited amount of time.
	/// </summary>
    public sealed class TimeLock
    {
		private long _nextAvailable;

		/// <summary>
		/// The duration a lock lasts befor expiring.
		/// </summary>
		public TimeSpan LockDuration { get; }

		public TimeLock(TimeSpan lockDuration)
		{
			LockDuration = lockDuration;
		}

		/// <summary>
		/// Attempt to obtain the lock for the a duration of <see cref="LockDuration"/>.
		/// </summary>
		public bool Obtain()
		{
			var now = DateTime.UtcNow.Ticks;
			var assumed = Interlocked.Read(ref _nextAvailable);
			if (now < assumed)
				return false; // Lock is still held

			var next = now + LockDuration.Ticks;
			var actual = Interlocked.CompareExchange(ref _nextAvailable, next, assumed);
			return actual == assumed;
		}

		/// <summary>
		/// Release the current lock, regarless of owner.
		/// </summary>
		public void Release()
		{
			Interlocked.Exchange(ref _nextAvailable, 0);
		}
    }
}
