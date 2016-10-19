using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using System.Collections.Generic;
using SharpChannels;
using System.Linq;

namespace SharpChannels.Tests
{
    [TestClass]
    public class SimpleTests
    {
        [TestMethod]
        public async Task SimpleSendAndReceive_Unbuffered()
        {
            List<int> results = new List<int>();
            for (int x = 0; x < 100000; ++x)
            {
                results.Clear();
                var channel = new Channel<int>();
                var sender = Task.Run(async () =>
                {
                    for (int i = 0; i < 10; ++i)
                    {
                        await channel.SendAsync(i + 1);
                    }
                    channel.Close();
                });

                while (!channel.IsClosed)
                {
                    var value = await channel.ReceiveAsync();
                    // 0 means that the channel has been closed
                    if (value != 0)
                    {
                        results.Add(value);
                    }
                }

                Assert.IsTrue(Enumerable.SequenceEqual(new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, results));
            }
        }

        [TestMethod]
        public async Task SimpleSendAndReceive_Buffered()
        {
            List<int> results = new List<int>();
            for (int x = 0; x < 100000; ++x)
            {
                results.Clear();
                var channel = new Channel<int>(5);
                var sender = Task.Run(async () =>
                {
                    for (int i = 0; i < 10; ++i)
                    {
                        await channel.SendAsync(i + 1);
                    }
                    channel.Close();
                });

                while (!channel.IsClosed)
                {
                    var value = await channel.ReceiveAsync();
                    // 0 means that the channel has been closed
                    if (value != 0)
                    {
                        results.Add(value);
                    }
                }

                Assert.IsTrue(Enumerable.SequenceEqual(new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }, results));
            }
        }
    }
}
