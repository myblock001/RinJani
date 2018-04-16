using System.Collections.Generic;

namespace Rinjani
{
    public class ConfigRoot
    {
        public bool DemoMode { get; set; }
        public bool Arbitrage { get; set; } //套利功能
        public bool LiquidBot { get; set; }//流动性机器人功能
        public decimal VolumeRatio { get; set; } //下单量比例20%,LiquidBot为true时有效
        public decimal RemovalRatio { get; set; } //去除比例，[-RemovalRatio,RemovalRatio]之间的订单不搬,LiquidBot为true时有效
        public decimal PriceMergeSize { get; set; } = 10;
        public decimal MaxSize { get; set; }
        public decimal MinSize { get; set; }
        public decimal ArbitragePoint { get; set; } //套利开启点 百分比
        public decimal CancelPoint { get; set; } //取消点 百分比
        public decimal StopPoint { get; set; } //止损点 百分比
        public int IterationInterval { get; set; } = 2000;
        public int BalanceRefreshInterval { get; set; } = 10000;
        public int SleepAfterSend { get; set; } = 10000;
        public int QuoteRefreshInterval { get; set; } = 2000;
        public List<BrokerConfig> Brokers { get; set; }
        public int MaxRetryCount { get; set; } = 30;
        public bool CancelAllOrders { get; set; }
        public int OrderStatusCheckInterval { get; set; } = 3000;
    }
}