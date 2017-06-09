using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace ConcurrentPatterns.Tests
{
    public class DelayTaskSourceTests
    {

        [Fact]
        public async Task Full_delay_without_cancellation()
        {
            var dts = new DelayTaskSource(CancellationToken.None);
            var before = DateTime.Now;
            var duration = TimeSpan.FromMilliseconds(10);
            await dts.Delay(duration);
            var after = DateTime.Now;
            Assert.True(after >= before + duration, "Delay must be at least as long as requested");
        }

        [Fact]
        public async Task Short_delay_if_interrupted()
        {
            var dts = new DelayTaskSource(CancellationToken.None);

            async Task AttemptDelay()
            {
                for (int i = 0; i < 100; i++)
                {
                    var before = DateTime.Now;
                    await dts.Delay(TimeSpan.FromSeconds(1));
                    var after = DateTime.Now;
                    Assert.True(after - before < TimeSpan.FromMilliseconds(10), "Delay should be cancelled quickly");
                }
            }

            void KeepCancelling(CancellationToken ct)
            {
                while (!ct.IsCancellationRequested)
                {
                    dts.Cancel(); 
                }
            }

            var cts = new CancellationTokenSource();
            var t = Task.Run(() => KeepCancelling(cts.Token), CancellationToken.None);

            var tasks = new Task[10];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(AttemptDelay, CancellationToken.None);
            }

            await Task.WhenAll(tasks);

            cts.Cancel();

            await t;
        }

        [Fact]
        public async Task Parent_cancellation_is_watched()
        {
            var threw = false;
            var cts = new CancellationTokenSource(100);
            var dts = new DelayTaskSource(cts.Token);
            var before = DateTime.Now;
            try
            {
                await dts.Delay(TimeSpan.FromSeconds(1));
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }
            var after = DateTime.Now;
            var msWaited = (after - before).TotalMilliseconds;
            Assert.InRange(msWaited, 75, 125);
            Assert.True(threw, "Expected Delay to throw OperationCanceledException");
        }

    }
}
