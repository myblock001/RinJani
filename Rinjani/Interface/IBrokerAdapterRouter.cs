using System.Collections.Generic;

namespace Rinjani
{
    public interface IBrokerAdapterRouter
    {
        void Send(Order order);
        void Refresh(Order order);
        void Cancel(Order order);
        BrokerBalance GetBalance(Broker broker);
        IList<Quote> FetchQuotes(Broker broker);
    }
}