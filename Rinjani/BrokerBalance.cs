using Rinjani.Properties;

namespace Rinjani
{
    public class BrokerBalance
    {
        public Broker Broker { get; set; }
        public decimal Leg1 { get; set; }
        public decimal Leg2 { get; set; }

        public override string ToString()
        {
            return
                $"{Broker.ToString(),10}: {Leg1,5:0.00} Leg2,{Leg2,5:0.00} Leg2";
        }
    }
}