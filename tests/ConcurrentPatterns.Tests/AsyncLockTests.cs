using System;
using System.Threading.Tasks;
using Xunit;

namespace ConcurrentPatterns.Tests
{
    public class AsyncLockTests
    {

        [Fact]
        public async Task Happy_day()
        {
            AsyncLock @lock = new AsyncLock();

            Guid expected = Guid.NewGuid();
            Guid id = expected;
            int numIterations = 0;

            const int bytesInGuid = 16;
            const int numbersInByte = 256;
            const int numCycles = 100;

            var tasks = new Task[bytesInGuid];
            for (int byteIndex = 0; byteIndex < tasks.Length; byteIndex++)
            {
                var myIndex = byteIndex;
                tasks[myIndex] = Task.Run(async () =>
                {
                    using (await @lock.WaitAsync())
                    {
                        // if everything works perfectly, the guid should end where it started
                        for (int i = 0; i < numbersInByte * numCycles; i++)
                        {
                            numIterations++;
                            var bytes = id.ToByteArray();
                            bytes[myIndex] = (byte)((1 + bytes[myIndex]) % 256);
                            id = new Guid(bytes); 
                        }
                    }
                });
            }
            await Task.WhenAll(tasks);

            Assert.Equal(expected, id);
            Assert.Equal(bytesInGuid * numbersInByte * numCycles, numIterations);
        }
    }
}
