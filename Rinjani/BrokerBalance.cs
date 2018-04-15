using Rinjani.Properties;

namespace Rinjani
{
    public class BrokerBalance
    {
        public Broker Broker { get; set; }
        public decimal Hsr { get; set; }
        public decimal Cash { get; set; }

        public override string ToString()
        {
            return
                $"{Broker.ToString(),10}: {Hsr,5:0.00} HSR,{Cash,5:0.00} Cash";
        }
    }
}