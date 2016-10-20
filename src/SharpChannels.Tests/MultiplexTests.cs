using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SharpChannels.Tests
{
    [TestClass]
    public class MultiplexTests
    {
        [TestMethod]
        public async Task SimpleCase_ReceiveWins()
        {
            Channel<string> channelForReceive = new Channel<string>(1);
            Channel<string> channelForSend = new Channel<string>(1);

            await channelForReceive.SendAsync("test");
            string received = null;
            bool defaulted = false;
            new Multiplex()
                .CaseReceive(channelForReceive, v => received = v)
                .CaseSend(channelForSend, "should not happen")
                .Default(() => defaulted = true);

            Assert.AreEqual("test", received);
            Assert.IsFalse(defaulted);
            Assert.AreEqual(0, channelForReceive.BufferedCount);
            Assert.AreEqual(0, channelForSend.BufferedCount);
        }

        [TestMethod]
        public async Task AsyncCase_ReceiveWins()
        {
            Channel<string> channelForReceive = new Channel<string>();
            Channel<string> channelForSend = new Channel<string>();

            string received = null;
            bool sendCalled = false;
            var t = Task.Run(async () =>
            {
                await Task.Delay(500);
                await channelForReceive.SendAsync("test");
                string _ = null;
                sendCalled = channelForSend.TryReceive(out _);
            });
            await new Multiplex()
                .CaseReceive(channelForReceive, v => received = v)
                .CaseSend(channelForSend, "should not happen");
            await t;
            Assert.AreEqual("test", received);
            Assert.IsFalse(sendCalled);
            Assert.AreEqual(0, channelForReceive.BufferedCount);
            Assert.AreEqual(0, channelForSend.BufferedCount);
        }
        [TestMethod]
        public async Task AsyncCase_SendWins()
        {
            Channel<string> channelForReceive = new Channel<string>();
            Channel<string> channelForSend = new Channel<string>();

            string received = null;
            bool sendCalled = false;
            var t = Task.Run(async () =>
            {
                await Task.Delay(500);
                string _ = null;
                sendCalled = channelForSend.TryReceive(out _);
                await channelForReceive.SendAsync("test");
            });
            await new Multiplex()
                .CaseReceive(channelForReceive, v => received = v)
                .CaseSend(channelForSend, "sent");
            await Task.Delay(200);
            Assert.IsNull(received);
            Assert.IsTrue(sendCalled);
            Assert.AreEqual(0, channelForReceive.BufferedCount);
            Assert.AreEqual(0, channelForSend.BufferedCount);
        }

        [TestMethod]
        public async Task SimpleCase_ClosedReceiveWins()
        {
            Channel<string> channelForReceive = new Channel<string>(1);
            Channel<string> channelForSend = new Channel<string>();
            Channel<string> closedChannel = new Channel<string>();
            closedChannel.Close();

            string received = null;
            bool defaulted = false;
            bool closedWin = false;
            new Multiplex()
                .CaseReceive(channelForReceive, v => received = v)
                .CaseSend(channelForSend, "should not happen")
                .CaseReceive(closedChannel, _ => closedWin = true)
                .Default(() => defaulted = true);

            Assert.IsNull(received);
            Assert.IsFalse(defaulted);
            Assert.AreEqual(0, channelForReceive.BufferedCount);
            Assert.AreEqual(0, channelForSend.BufferedCount);
            Assert.IsTrue(closedWin);
        }

        [TestMethod]
        public async Task SimpleCase_SendWins()
        {
            Channel<string> channelForReceive = new Channel<string>(1);
            Channel<string> channelForSend = new Channel<string>(1);
            Channel<string> closedChannel = new Channel<string>();
            closedChannel.Close();

            string received = null;
            bool defaulted = false;
            bool closedWin = false;
            new Multiplex()
                .CaseReceive(channelForReceive, v => received = v)
                .CaseSend(channelForSend, "test")
                .CaseReceive(closedChannel, _ => closedWin = true)
                .Default(() => defaulted = true);

            Assert.IsNull(received);
            Assert.IsFalse(defaulted);
            Assert.AreEqual(0, channelForReceive.BufferedCount);
            Assert.AreEqual(1, channelForSend.BufferedCount);
            Assert.IsFalse(closedWin);
            string oVal;
            Assert.IsTrue(channelForSend.TryReceive(out oVal));
            Assert.AreEqual("test", oVal);
        }

        [TestMethod]
        public async Task SimpleCase_DefaultWins()
        {
            Channel<string> channelForReceive = new Channel<string>(1);
            Channel<string> channelForSend = new Channel<string>();
            Channel<string> closedChannel = new Channel<string>();

            string received = null;
            bool defaulted = false;
            bool closedWin = false;
            new Multiplex()
                .CaseReceive(channelForReceive, v => received = v)
                .CaseSend(channelForSend, "should not happen")
                .CaseReceive(closedChannel, _ => closedWin = true)
                .Default(() => defaulted = true);

            Assert.IsNull(received);
            Assert.IsTrue(defaulted);
            Assert.AreEqual(0, channelForReceive.BufferedCount);
            Assert.AreEqual(0, channelForSend.BufferedCount);
            Assert.IsFalse(closedWin);
        }

    }
}
