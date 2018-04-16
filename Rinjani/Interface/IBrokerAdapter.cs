using System.Collections.Generic;

namespace Rinjani
{
    public interface IBrokerAdapter
    {
        Broker Broker { get; }
        void Send(Order order);
        void Refresh(Order order);
        void Cancel(Order order);
        string GetOrdersState(int pageIndex,int tradeType);
        BrokerBalance GetBalance();
        IList<Quote> FetchQuotes();
    }
}