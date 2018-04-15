using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Rinjani.Tests
{
    [TestClass]
    public class QuoteAggregatorTest
    {
        [TestMethod]
        public void TestQuoteAggregator()
        {
            var config = new ConfigRoot
            {
                Brokers = new List<BrokerConfig>
                {
                    new BrokerConfig()
                    {
                        Broker = Broker.Zb,
                        Enabled = true
                    },
                    new BrokerConfig
                    {
                        Broker = Broker.Hpx,
                        Enabled = true,
                    }
                }
            };
            var mConfigRepo = new Mock<IConfigStore>();
            mConfigRepo.Setup(x => x.Config).Returns(config);
            var configStore = mConfigRepo.Object;

            var mZbBa = new Mock<IBrokerAdapter>();
            mZbBa.Setup(x => x.Broker).Returns(Broker.Zb);
            var quotes1 = new List<Quote>
            {
                new Quote(Broker.Zb, QuoteSide.Ask, 500000, 0.1m, 0.1m),

                new Quote(Broker.Zb, QuoteSide.Ask, 500001, 0.01m, 0.01m)
            };
            mZbBa.Setup(x => x.FetchQuotes()).Returns(quotes1);

            var mHpxBa = new Mock<IBrokerAdapter>();
            mHpxBa.Setup(x => x.Broker).Returns(Broker.Hpx);
            mHpxBa.Setup(x => x.FetchQuotes()).Returns(new List<Quote>());
        }

        [TestMethod]
        public void TestQuoteAggregatorWithDisabledBa()
        {
            var config = new ConfigRoot
            {
                Brokers = new List<BrokerConfig>
                {
                    new BrokerConfig()
                    {
                        Broker = Broker.Zb,
                        Enabled = false
                    },
                    new BrokerConfig
                    {
                        Broker = Broker.Hpx,
                        Enabled = true
                    }
                }
            };
            var mConfigRepo = new Mock<IConfigStore>();
            mConfigRepo.Setup(x => x.Config).Returns(config);
            var configStore = mConfigRepo.Object;

            var mZbBa = new Mock<IBrokerAdapter>();
            mZbBa.Setup(x => x.Broker).Returns(Broker.Zb);
            var quotes1 = new List<Quote>
            {
                new Quote(Broker.Zb, QuoteSide.Ask, 500000, 0.1m ,0.1m),

                new Quote(Broker.Zb, QuoteSide.Ask, 500001, 0.01m, 0.01m)
            };
            mZbBa.Setup(x => x.FetchQuotes()).Returns(quotes1);

            var mHpxBa = new Mock<IBrokerAdapter>();
            mHpxBa.Setup(x => x.Broker).Returns(Broker.Hpx);
            mHpxBa.Setup(x => x.FetchQuotes()).Returns(new List<Quote>());
        }
    }
}