using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using Moq;

namespace Rinjani.Tests
{
    [TestClass]
    public class BrokerAdapterRouterTest
    {
        private List<IBrokerAdapter> _brokerAdapters;
        private Mock<IBrokerAdapter> _baZb;
        private Mock<IBrokerAdapter> _baHpx;
        private BrokerAdapterRouter _target;

        [TestInitialize]
        public void Init()
        {
            _baZb = new Mock<IBrokerAdapter>();
            _baZb.Setup(x => x.Broker).Returns(Broker.Zb);
            _baZb.Setup(x => x.Send(It.IsAny<Order>()));

            _baHpx = new Mock<IBrokerAdapter>();
            _baHpx.Setup(x => x.Broker).Returns(Broker.Hpx);
            _baHpx.Setup(x => x.Send(It.IsAny<Order>()));

            _brokerAdapters = new List<IBrokerAdapter>() { _baZb.Object, _baHpx.Object };
            _target = new BrokerAdapterRouter(_brokerAdapters);
        }

        [TestMethod]
        public void SendTest()
        {            
            var order = new Order(Broker.Zb, OrderSide.Buy, 0.001m, 500000, CashMarginType.Cash, OrderType.Limit, 0);
            _target.Send(order);
            _baZb.Verify(x => x.Send(order));         
        }

        [TestMethod]
        public void FetchQuoteTest()
        {
            _target.FetchQuotes(Broker.Hpx);
            _baHpx.Verify(x => x.FetchQuotes());
        }

        [TestMethod]
        public void GetBalanceTest()
        {
            _target.GetBalance(Broker.Hpx);
            _baHpx.Verify(x => x.GetBalance());
        }

        [TestMethod]
        public void GetOrderStateTest()
        {
            var order = new Order(Broker.Hpx, OrderSide.Buy, 0.001m, 500000, CashMarginType.Cash, OrderType.Limit, 0);
            _target.Refresh(order);
            _baHpx.Verify(x => x.Refresh(order));
        }
    }
}
