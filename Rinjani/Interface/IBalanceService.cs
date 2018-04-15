using System;
using System.Collections.Generic;

namespace Rinjani
{
    public interface IBalanceService : IDisposable
    {
        IDictionary<Broker, BrokerBalance> BalanceMap { get; }
    }
}