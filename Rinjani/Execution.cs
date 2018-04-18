using System;

namespace Rinjani
{
    public class Execution
    {
        public Execution(Order order)
        {
            Broker = order.Broker;
            BrokerOrderId = order.BrokerOrderId;
            Side = order.Side;
            Symbol = order.Symbol;
        }

        public Guid Id { get; } = Guid.NewGuid();
        public Broker Broker { get; set; } = Broker.None;
        public string BrokerOrderId { get; set; }
        public decimal Size { get; set; }
        public decimal Price { get; set; }
        public DateTime ExecTime { get; set; }
        public OrderSide Side { get; set; }
        public string Symbol { get; set; }
    }
}