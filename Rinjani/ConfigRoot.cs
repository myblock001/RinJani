using System.Collections.Generic;

namespace Rinjani
{
    public class ConfigRoot
    {
        public bool DemoMode { get; set; }
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
        public int OrderStatusCheckInterval { get; set; } = 3000;
    }
}