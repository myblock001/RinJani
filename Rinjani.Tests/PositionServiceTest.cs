using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Rinjani.Tests
{
    [TestClass]
    public class PositionServiceTest
    {
        [TestMethod]
        public void TestPositionService()
        {
            var config = new ConfigRoot
            {
                Brokers = new List<BrokerConfig>
                {
                    new BrokerConfig
                    {
                        Broker = Broker.Hpx,
                        Enabled = true
                    },
                    new BrokerConfig
                    {
                        Broker = Broker.Zb,
                        Enabled = true
                    }
                }
            };
            var mConfigRepo = new Mock<IConfigStore>();
            mConfigRepo.Setup(x => x.Config).Returns(config);
            var configStore = mConfigRepo.Object;
            var mBARouter = new Mock<IBrokerAdapterRouter>();
            var baRouter = mBARouter.Object;
            var mTimer = new Mock<ITimer>();

            var ps = new BalanceService(configStore, baRouter, mTimer.Object);
            var positions = ps.BalanceMap.Values.ToList();
            var ccPos = positions.First(x => x.Broker == Broker.Zb);

            Assert.IsTrue(positions.Count == 2);
        }
    }
}