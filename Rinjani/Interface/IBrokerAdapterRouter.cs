using System.Collections.Generic;

namespace Rinjani
{
    public interface IBrokerAdapterRouter
    {
        void Send(Order order);
        void Refresh(Order order);
        void Cancel(Order order);
        string GetOrdersState(int pageIndex, int tradeType,Broker broker);
        BrokerBalance GetBalance(Broker broker);
        IList<Quote> FetchQuotes(Broker broker);
    }
}