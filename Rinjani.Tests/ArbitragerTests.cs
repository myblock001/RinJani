using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinjani;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using RestSharp;
using Rinjani.Hpx;
using Newtonsoft.Json.Linq;
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
            string response=ba.GetOrdersState(1,0) ;
            JObject j = JObject.Parse(response);
            JArray ja= JArray.Parse(j["data"].ToString());
            List<OrderStateReply> ordersState = ja.ToObject<List<OrderStateReply>>();
        }
    }
}