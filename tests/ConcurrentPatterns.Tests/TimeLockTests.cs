using System;
using System.Threading;
using Xunit;

namespace ConcurrentPatterns.Tests
{
    public class TimeLockTests
    {

        [Fact]
        public void Ctor_throws_negative_timespan()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TimeLock(TimeSpan.FromMilliseconds(-1)));
        }

        [Fact]
        public void Ctor_allows_zero_timespan()
        {
            // No exception thrown
            Assert.NotNull(new TimeLock(TimeSpan.Zero));
        }

        [Fact]
        public void LockDuration_has_correct_value()
        {
            var duration = TimeSpan.FromMilliseconds(12345);
            var timeLock = new TimeLock(duration);
            Assert.Equal(duration, timeLock.LockDuration);
        }
        
        [Fact]
        public void Obtain_waits_long_enough()
        {
            var lockDuration = TimeSpan.FromMilliseconds(10);

            var timeLock = new TimeLock(lockDuration);

            timeLock.Obtain();
            var obtained = DateTime.Now;

            var spinwait = new SpinWait();
            while (!timeLock.Obtain())
            {
                spinwait.SpinOnce();

                // Avoid infinite loop
                Assert.True(DateTime.Now < obtained + lockDuration + lockDuration);
            }

            var released = DateTime.Now;
            
            Assert.True(Math.Abs((released - obtained - lockDuration).TotalMilliseconds) < 2,
                        "Lock should be released at around the right time");
        }
    }
}
