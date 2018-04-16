using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NLog;
using RestSharp;
using Newtonsoft.Json.Linq;

namespace Rinjani.Zb
{
    public class BrokerAdapter : IBrokerAdapter
    {
        private const string ApiRootQuotes = "http://api.zb.com";
        private const string ApiRootTrade = "https://trade.zb.com";
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly BrokerConfig _config;
        private readonly IRestClient _restClient;
        private Uri baseUrlQuotes = null;
        private Uri baseUrlTrade = null;

        public BrokerAdapter(IRestClient restClient, IConfigStore configStore)
        {
            if (configStore == null)
            {
                throw new ArgumentNullException(nameof(configStore));
            }
            _config = configStore.Config.Brokers.First(b => b.Broker == Broker);
            baseUrlQuotes= new Uri(ApiRootQuotes);
            baseUrlTrade = new Uri(ApiRootTrade);
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
            _restClient.BaseUrl = baseUrlQuotes;
        }

        public void SetBaseUrl(string type)
        {
            if(type.ToLower().Trim()== "quote")
                _restClient.BaseUrl = baseUrlQuotes;
            else
                _restClient.BaseUrl = baseUrlTrade;
        }

        public Broker Broker => Broker.Zb;

        public void Send(Order order)
        {
            if (order.Broker != Broker)
            {
                throw new InvalidOperationException();
            }
            SendOrderParam param = new SendOrderParam(order);
            var reply = Send(param);
            if(reply.code.Trim()!="1000")
            {
                order.Status = OrderStatus.Rejected;
                Log.Info("Zb Send: " + reply.message);
                return;
            }
            order.BrokerOrderId = reply.id;
            order.Status = OrderStatus.New;
            order.SentTime = DateTime.Now;
            order.LastUpdated = DateTime.Now;
        }

        public void Refresh(Order order)
        {
            var reply = GetOrderState(order.BrokerOrderId);
            reply.SetOrder(order);
        }

        public void Cancel(Order order)
        {
            Cancel(order.BrokerOrderId);
            order.LastUpdated = DateTime.Now;
            order.Status = OrderStatus.Canceled;
        }

        public BrokerBalance GetBalance()
        {
            try
            {
                Log.Debug($"Zb GetBalance Start ...");
                var path = "/api/getAccountInfo?";
                string body = "accesskey=" + _config.Key + "&method=getAccountInfo";
                path += body;
                var req = BuildRequest(path, "GET", body);
                RestUtil.LogRestRequest(req);
                SetBaseUrl("trade");
                var response = _restClient.Execute(req);
                if (response == null || response.StatusCode == 0)
                {
                    Log.Debug($"Zb GetBalance response is null or failed ...");
                    return null;
                }
                JObject j = JObject.Parse(response.Content);
                j = JObject.Parse(j["result"].ToString());
                JArray jar = JArray.Parse(j["coins"].ToString());
                JObject jhsr = null;
                JObject jqc = null;
                foreach (JObject jj in jar)
                {
                    if (jj["enName"].ToString().ToUpper() == "HSR")
                    {
                        jhsr = jj;
                    }
                    if (jj["enName"].ToString().ToUpper() == "QC")
                    {
                        jqc = jj;
                    }
                    if (jhsr != null && jqc != null)
                        break;
                }
                BrokerBalance bb = new BrokerBalance();
                bb.Broker = Broker;
                bb.Hsr = decimal.Parse(jhsr["available"].ToString());
                bb.Cash = decimal.Parse(jqc["available"].ToString());
                Log.Debug($"Zb GetBalance End ...");
                return bb;
            }
            catch (Exception ex)
            {
                Log.Debug($"Zb GetBalance response is null or failed ...{ex.Message}");
                return null;
            }
        }

        public string FetchTicker()
        {
            Log.Debug($"Getting ticker from {_config.Broker}...");
            var path = "data/v1/ticker?market=hsr_qc";
            var req = RestUtil.CreateJsonRestRequest(path);
            SetBaseUrl("quote");
            var response = _restClient.Execute(req);
            return response.Content;
        }

        public IList<Quote> FetchQuotes()
        {
            try
            {
                Log.Debug($"Getting depth from {_config.Broker}...");
                var path = "data/v1/depth?market=hsr_qc&size=50";
                var req = RestUtil.CreateJsonRestRequest(path);
                SetBaseUrl("quote");
                var response = _restClient.Execute(req);
                if (response.ErrorException != null)
                {
                    throw response.ErrorException;
                }
                Log.Debug($"Received depth from {_config.Broker}.");
                Depth depth = new Depth() { quotesJson = response.Content, tickerJson= FetchTicker() };
                var quotes = depth.ToQuotes();
                return quotes ?? new List<Quote>();
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
                Log.Debug(ex);
                return new List<Quote>();
            }
        }

        private SendReply Send(SendOrderParam param)
        {
            var path = "/api/order?";
            int tradetype = param.side == "buy" ? 1 : 0;
            string body = "accesskey=" + _config.Key + $"&amount={param.quantity}&currency=hsr_qc&method=order&price={param.price}&tradeType={tradetype}";
            path += body;
            var req = BuildRequest(path, "GET", body);
            RestUtil.LogRestRequest(req);
            SetBaseUrl("trade");
            var response = _restClient.Execute(req);
            if (response == null || response.StatusCode == 0)
            {
                Log.Debug($"Zb Send response is null or failed ...");
                return new SendReply() { code ="-1" };
            }
            JObject j = JObject.Parse(response.Content);
            SendReply reply = j.ToObject<SendReply>();
            return reply;
        }

        private OrderStateReply GetOrderState(string id)
        {
            var path = "/api/getOrder?";
            string body = "accesskey=" + _config.Key + $"&currency=hsr_qc&id={id}&method=getOrder";
            path += body;
            var req = BuildRequest(path, "GET", body);
            RestUtil.LogRestRequest(req);
            SetBaseUrl("trade");
            var response = _restClient.Execute(req);
            if (response == null || response.StatusCode == 0)
            {
                Log.Debug($"Zb GetOrderState response is null or failed ...");
                return null;
            }
            JObject j = JObject.Parse(response.Content);
            OrderStateReply reply = j.ToObject<OrderStateReply>();
            return reply;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tradeType"> 挂单类型 1/0[buy/sell]</param>
        /// <returns></returns>
        public string GetOrdersState(int pageIndex, int tradeType)
        {
            var path = "/api/getOrders?";
            string body = "accesskey=" + _config.Key + $"&currency=hsr_qc&method=getOrders&pageIndex={pageIndex}&tradeType={tradeType}";
            path += body;
            var req = BuildRequest(path, "GET", body);
            RestUtil.LogRestRequest(req);
            SetBaseUrl("trade");
            var response = _restClient.Execute(req);
            if (response == null || response.StatusCode == 0)
            {
                Log.Debug($"Zb GetOrderaState response is null or failed ...");
                return null;
            }
            return response.Content;
        }

        private void Cancel(string orderId)
        {
            var path = "/api/cancelOrder?";
            string body = "accesskey=" + _config.Key + $"&currency=hsr_qc&id={orderId}&method=cancelOrder";
            path += body;
            var req = BuildRequest(path, "GET", body);
            SetBaseUrl("trade");
            RestUtil.LogRestRequest(req);
            var response = _restClient.Execute(req);
            if (response == null || response.StatusCode == 0)
            {
                Log.Debug($"Zb Cancel response is null or failed ...");
                Cancel(orderId);
            }
        }

        private RestRequest BuildRequest(string path, string method = "GET", string body = "")
        {
            if(!string.IsNullOrEmpty(body))
            {
                string secretkey = _config.Secret;
                secretkey = Util.digest(secretkey);
                String sign = Util.hmacSign(body, secretkey);
                DateTime timeStamp = new DateTime(1970, 1, 1);
                //得到1970年的时间戳
                long stamp = (DateTime.UtcNow.Ticks - timeStamp.Ticks) / 10000;
                path+= "&sign=" + sign + "&reqTime=" + stamp;
            }
            var req = RestUtil.CreateJsonRestRequest(path);
            req.Method = Util.ParseEnum<Method>(method);
            return req;
        }
    }
}