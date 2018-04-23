using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using NLog;

namespace Rinjani
{
    public class BalanceService : IBalanceService
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly IBrokerAdapterRouter _brokerAdapterRouter;
        private readonly IConfigStore _configStore;

        public BalanceService(IConfigStore configStore, IBrokerAdapterRouter brokerAdapterRouter,
            ITimer timer)
        {
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _brokerAdapterRouter = brokerAdapterRouter ?? throw new ArgumentNullException(nameof(brokerAdapterRouter));
        }

        public BrokerBalance BalanceZb { get; private set; } = new BrokerBalance();
        public BrokerBalance BalanceHpx { get; private set; } = new BrokerBalance();

        private void LogBalances(BrokerBalance balance)
        {
            Log.Info($"{balance.Broker}  Leg1={balance.Leg1} Leg2={balance.Leg2}");
        }

        public void GetBalance(Broker broker)
        {
            if (broker == Broker.Zb)
                BalanceZb = _brokerAdapterRouter.GetBalance(broker);
            else
                BalanceHpx = _brokerAdapterRouter.GetBalance(broker);
        }
    }
}