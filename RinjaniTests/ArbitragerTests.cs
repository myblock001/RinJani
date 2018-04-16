using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinjani;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rinjani.Hpx;
using RestSharp;

namespace Rinjani.Tests
{
    [TestClass()]
    public class ArbitragerTests
    {
        [TestMethod()]
        public void GetOrdersStateTest()
        {
            var configStore = new JsonConfigStore("config.json", new List<IConfigValidator>());
            var ba = new BrokerAdapter(new RestClient(), configStore);
            var order = new Order { Broker = Broker.Hpx, BrokerOrderId = "2157479", Size = 0.01m };
            ba.Cancel(order);
        }
    }
}