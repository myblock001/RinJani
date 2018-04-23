using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using NLog;

namespace Rinjani
{
    public class QuoteAggregator : IQuoteAggregator
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly IList<IBrokerAdapter> _brokerAdapters;
        private readonly ConfigRoot _config;
        private IList<Quote> _hpxquotes;
        private IList<Quote> _zbquotes;
        public QuoteAggregator(IConfigStore configStore, IList<IBrokerAdapter> brokerAdapters, ITimer timer)
        {
            if (brokerAdapters == null)
            {
                throw new ArgumentNullException(nameof(brokerAdapters));
            }
            _config = configStore?.Config ?? throw new ArgumentNullException(nameof(configStore));
            _brokerAdapters = Util.GetEnabledBrokerAdapters(brokerAdapters, configStore);
        }

        public IList<Quote> HpxQuotes
        {
            get => _hpxquotes;
            private set
            {
                _hpxquotes = value;
            }
        }

        public IList<Quote> ZbQuotes
        {
            get => _zbquotes;
            private set
            {
                _zbquotes = value;
            }
        }
        private object hpx = new object();
        public void HpxAggregate()
        {
            lock (hpx)
            {
                Log.Debug("Aggregating hpxquotes...");
                HpxQuotes = _brokerAdapters[1].FetchQuotes();
                Log.Debug("Aggregated.");
            }
        }
        private object zb = new object();
        public void ZbAggregate()
        {
            lock (zb)
            {
                Log.Debug("Aggregating quotes...");
                ZbQuotes = _brokerAdapters[0].FetchQuotes();
                Log.Debug("Aggregated.");
            }
        }

        public IList<Quote> GetHpxQuote()
        {
            lock (hpx)
            {
                if (HpxQuotes == null)
                    return new List<Quote>();
                return HpxQuotes;
            }
        }

        public IList<Quote> GetZbQuote()
        {
            lock (zb)
            {
                if (ZbQuotes == null)
                    return new List<Quote>();
                return ZbQuotes;
            }
        }
    }
}