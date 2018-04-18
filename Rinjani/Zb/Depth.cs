using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Rinjani.Zb
{
    public class Depth
    {
        public string tickerJson { get; set; }
        public string quotesJson { get; set; }
        public IList<Quote> ToQuotes()
        {
            var quotes = new List<Quote>();
            try
            {
                JObject j = JObject.Parse(quotesJson);
                JArray jasks = JArray.Parse(j["asks"].ToString());
                JArray jbids = JArray.Parse(j["bids"].ToString());
                List<List<decimal>> SellPriceLevels = jasks.ToObject<List<List<decimal>>>();
                List<List<decimal>> BuyPriceLevels = jbids.ToObject<List<List<decimal>>>();

                j = JObject.Parse(tickerJson);
                j = JObject.Parse(j["ticker"].ToString());
                decimal basePrice = decimal.Parse(j["last"].ToString());

                if (BuyPriceLevels != null)
                {
                    quotes.AddRange(BuyPriceLevels.Take(100).Select(x => new Quote
                    {
                        Broker = Broker.Zb,
                        Side = QuoteSide.Bid,
                        Price = x[0],
                        BasePrice = basePrice,
                        Volume = x[1]
                    }));
                }
                if (SellPriceLevels != null)
                {
                    quotes.AddRange(SellPriceLevels.Take(100).Select(x => new Quote
                    {
                        Broker = Broker.Zb,
                        Side = QuoteSide.Ask,
                        Price = x[0],
                        BasePrice = basePrice,
                        Volume = x[1]
                    }));
                }
            }
            catch { }
            return quotes;
        }
    }
}