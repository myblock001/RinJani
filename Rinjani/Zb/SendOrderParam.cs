using System;

namespace Rinjani.Zb
{
    public class SendOrderParam
    {
        public SendOrderParam(Order order)
        {
            string orderType;
            switch (order.Type)
            {
                case OrderType.Limit:
                    orderType = "limit";
                    price = order.Price;
                    break;
                case OrderType.Market:
                    orderType = "market";
                    price = 0;
                    break;
                default:
                    throw new NotSupportedException();
            }

            order_type = orderType;
            side = order.Side.ToString().ToLower();
            quantity = order.Size;
            order_direction = null;
        }

        public string order_type { get; set; }
        public int product_id { get; set; }
        public string side { get; set; }
        public decimal quantity { get; set; }
        public decimal price { get; set; }
        public int leverage_level { get; set; }
        public string order_direction { get; set; }
    }
}