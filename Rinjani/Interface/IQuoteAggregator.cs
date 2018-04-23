using System;
using System.Collections.Generic;

namespace Rinjani
{
    public interface IQuoteAggregator
    {
        void HpxAggregate();
        void ZbAggregate();
        IList<Quote> GetHpxQuote();
        IList<Quote> GetZbQuote();
    }
}