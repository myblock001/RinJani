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
            string response = ba.GetOrdersState(1, 1);
            JObject j = JObject.Parse(response);
            JArray ja = JArray.Parse(j["data"].ToString());
            List<OrderStateReply> ordersState = ja.ToObject<List<OrderStateReply>>();
            Order o = new Order();
            o.Size = 0.04m;
            ordersState[0].SetOrder(o);

        }

        [TestMethod()]
        public void ListTetsTest()
        {
            List<int> kk = new List<int> { 1, 2, 3, 4, 5, 6 };
            var Bestkk = kk.Where(q => q < 4).OrderBy(q => q).FirstOrDefault();
            kk.Remove(1);
            Bestkk = kk.Where(q => q < 4).OrderBy(q => q).FirstOrDefault();
            kk.Remove(3);
            var Bestkk11 = kk.Where(q => q < 4).OrderBy(q => q);
            Bestkk = Bestkk11.FirstOrDefault();
        }
    }
}