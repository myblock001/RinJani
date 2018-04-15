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
        //private readonly ITimer _timer;
        private bool _isRefreshing;

        public BalanceService(IConfigStore configStore, IBrokerAdapterRouter brokerAdapterRouter,
            ITimer timer)
        {
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _brokerAdapterRouter = brokerAdapterRouter ?? throw new ArgumentNullException(nameof(brokerAdapterRouter));
            //_timer = timer;
            //Util.StartTimer(timer, _configStore.Config.BalanceRefreshInterval, OnTimerTriggered);
            //Refresh();
        }

        public IDictionary<Broker, BrokerBalance> BalanceMap { get; private set; } =
            new Dictionary<Broker, BrokerBalance>();

        public void Dispose()
        {
            //_timer?.Dispose();
        }

        private void OnTimerTriggered(object sender, ElapsedEventArgs e)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (_isRefreshing)
            {
                return;
            }

            try
            {
                _isRefreshing = true;
                var config = _configStore.Config;
                var balanceMap = new Dictionary<Broker, BrokerBalance>();
                foreach (var brokerConfig in config.Brokers.Where(b => b.Enabled))
                {
                    BrokerBalance currentBalance = GetBalance(brokerConfig.Broker);
                    balanceMap.Add(brokerConfig.Broker, currentBalance);
                } 

                BalanceMap = balanceMap;
                LogBalances();
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private void LogBalances()
        {
            Log.Info(Util.Hr(21) + "BALANCE" + Util.Hr(21));
            foreach (var balance in BalanceMap)
            {
                Log.Info(balance.Value);
            }
            Log.Info(Util.Hr(50));
        }

        public void GetBalances()
        {
            Refresh();
        }

        private BrokerBalance GetBalance(Broker broker)
        {
            return _brokerAdapterRouter.GetBalance(broker);
        }
    }
}