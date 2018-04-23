using System;
using System.Collections.Generic;

namespace Rinjani
{
    public interface IBalanceService
    {
        BrokerBalance BalanceZb { get; }
        BrokerBalance BalanceHpx { get; }
        void GetBalance(Broker broker);
    }
}