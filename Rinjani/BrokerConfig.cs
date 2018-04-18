namespace Rinjani
{
    public class BrokerConfig
    {
        public Broker Broker { get; set; }
        public string Key { get; set; }
        public string Secret { get; set; }
        public bool Enabled { get; set; }
        public string Leg1 { get; set; }//交易对的第一个币种
        public string Leg2 { get; set; } //交易对的第二个币种,稳定货币
        public decimal Leg2ExRate { get; set; } //Leg2汇率
        public decimal Leg1ThresholdSendEmail { get; set; }//Leg1余额低于该值时，发送电子邮件到EmailAddress
        public decimal Leg2ThresholdSendEmail { get; set; }//Leg2余额低于该值时，发送电子邮件到EmailAddress
    }
}