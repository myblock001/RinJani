using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Rinjani
{
    public class Order
    {
        public Order(Broker broker, OrderSide side, decimal size, decimal price,
            OrderType orderType)
        {
            Broker = broker;
            Size = size;
            Side = side;
            Price = price;
            Type = orderType;
        }

        public Order()
        {
        }

        public Broker Broker { get; set; }
        public OrderSide Side { get; set; }
        public string Symbol { get; set; } = "";
        public OrderType Type { get; set; } = OrderType.Limit;
        public decimal Size { get; set; }
        public decimal Price { get; set; }
        public TimeInForce TimeInForce { get; set; } = TimeInForce.None;

        public Guid Id { get; } = Guid.NewGuid();
        public string BrokerOrderId { get; set; }
        public OrderStatus Status { get; set; } = OrderStatus.PendingNew;
        public decimal PendingSize => Size - FilledSize;
        public decimal FilledSize { get; set; }

        public decimal AverageFilledPrice => Executions.Count == 0
            ? 0
            : Executions.Sum(x => x.Size * x.Price) / Executions.Sum(x => x.Size);

        public DateTime CreationTime { get; set; } = DateTime.Now;
        public DateTime SentTime { get; set; }
        public DateTime LastUpdated { get; set; }
        public string ClosingMarginPositionId { get; set; }
        public IList<Execution> Executions { get; set; } = new List<Execution>();

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this, Formatting.Indented, new StringEnumConverter());
        }
    }
}