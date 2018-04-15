using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Rinjani.Hpx
{
    public class Depth
    {
        public class Item
        {
            public decimal amount { get; set; }
            public decimal price { get; set; }
        }
        public string quotesJson { get; set; }
        public IList<Quote> ToQuotes()
        {
            JObject j = JObject.Parse(quotesJson);
            j = JObject.Parse(j["data"].ToString());
            decimal basePrice = decimal.Parse(j["p_new"].ToString());
            JArray jasks = JArray.Parse(j["sells"].ToString());
            JArray jbids = JArray.Parse(j["buys"].ToString());
            List<Item> SellPriceLevels = jasks.ToObject<List<Item>>();
            List<Item> BuyPriceLevels = jbids.ToObject<List<Item>>();
            var quotes = new List<Quote>();
            if (BuyPriceLevels != null)
            {
                quotes.AddRange(BuyPriceLevels.Take(100).Select(x => new Quote
                {
                    Broker = Broker.Hpx,
                    Side = QuoteSide.Bid,
                    Price = x.price,
                    BasePrice = basePrice,
                    Volume = x.amount
                }));
            }
            if (SellPriceLevels != null)
            {
                quotes.AddRange(SellPriceLevels.Take(100).Select(x => new Quote
                {
                    Broker = Broker.Hpx,
                    Side = QuoteSide.Ask,
                    Price = x.price,
                    BasePrice=basePrice,
                    Volume = x.amount
                }));
            }
            return quotes;
        }
    }
}