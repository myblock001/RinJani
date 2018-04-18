using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NLog;
using RestSharp;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Web;
using System.IO;
using System.Text;

namespace Rinjani.Hpx
{
    public class BrokerAdapter : IBrokerAdapter
    {
        private const string ApiRoot = "https://api.hpx.com";
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly BrokerConfig _config;
        private readonly IRestClient _restClient;

        public BrokerAdapter(IRestClient restClient, IConfigStore configStore)
        {
            if (configStore == null)
            {
                throw new ArgumentNullException(nameof(configStore));
            }
            _config = configStore.Config.Brokers.First(b => b.Broker == Broker);
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
            _restClient.BaseUrl = new Uri(ApiRoot);
        }

        public Broker Broker => Broker.Hpx;

        public void Send(Order order)
        {
            if (order.Broker != Broker)
            {
                throw new InvalidOperationException();
            }
            SendOrderParam param = new SendOrderParam(order);
            var reply = Send(param);
            if (reply.code.Trim() == "3001")
            {
                order.BrokerOrderId = "0x3fffff";//余额不足
                return;
            }

            if (reply.code.Trim() != "0000")
            {
                order.Status = OrderStatus.Rejected;
                Log.Info("Hpx Send: " + reply.message);
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
            if (reply == null)
                return;
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
                Log.Debug("Hpx GetBalance Begin");
                var path = "/api/v2/getAccountInfo?";
                string body = "method=getAccountInfo&accesskey=" + _config.Key;
                path += body;
                var req = BuildRequest(path, "GET", body);
                RestUtil.LogRestRequest(req);
                var response = _restClient.Execute(req);
                if (response == null || response.StatusCode == 0)
                {
                    Log.Debug($"Hpx GetBalance response is null or failed ...");
                    return null;
                }
                JObject j = JObject.Parse(response.Content);
                j = JObject.Parse(j["data"].ToString());
                j = JObject.Parse(j["balance"].ToString());
                BrokerBalance bb = new BrokerBalance();
                bb.Broker = Broker;
                bb.Leg1 = decimal.Parse(j["HSR"].ToString());
                bb.Leg2 = decimal.Parse(j["CNYT"].ToString());
                Log.Debug("Hpx GetBalance End");
                return bb;
            }
            catch(Exception ex)
            {
                Log.Debug("Hpx GetBalance Exception:"+ex.Message);
                return null;
            }
        }

        public IList<Quote> FetchQuotes()
        {
            try
            {
                Log.Debug("Hpx FetchQuotes Begin");
                string url = "https://www.hpx.com/real/market/hsr_cnyt.html";
                string param = "event=addChannel&channel=real_depth&exchangeTypeCode=hsr_cnyt&buysellcount=50&successcount=50&mergeType=1e-8&token=";
                string content = HttpPost(url, param);
                Log.Debug($"Received depth from {_config.Broker}.");
                Depth depth = new Depth() { quotesJson = content };
                var quotes = depth.ToQuotes();
                Log.Debug("Hpx FetchQuotes End");
                return quotes ?? new List<Quote>();
            }
            catch(Exception ex)
            {
                Log.Debug($"Hpx FetchQuotes Exception:" +ex.Message);
                return new List<Quote>();
            }
        }

        private SendReply Send(SendOrderParam param)
        {
            try
            {
                Log.Debug("Hpx Send Begin");
                var path = "/api/v2/order?";
                int tradetype = param.side == "buy" ? 0 : 1;
                string body = "method=order&accesskey=" + _config.Key + $"&amount={param.quantity}&currency=hsr_cnyt&price={param.price}&tradeType={tradetype}";
                path += body;
                var req = BuildRequest(path, "GET", body);
                RestUtil.LogRestRequest(req);
                var response = _restClient.Execute(req);
                if (response == null || response.StatusCode == 0)
                {
                    Log.Debug($"Hpx Send response is null or failed ...");
                    return new SendReply() { code = "-1" };
                }
                JObject j = JObject.Parse(response.Content);
                SendReply reply = j.ToObject<SendReply>();
                Log.Debug("Hpx Send End");
                return reply;
            }
            catch(Exception ex)
            {
                Log.Debug($"Hpx Send Exception:" + ex.Message);
                return null;
            }
        }

        private OrderStateReply GetOrderState(string id)
        {
            try
            {
                Log.Debug("Hpx GetOrderState Begin");
                var path = "/api/v2/getOrder?";
                string body = "method=getOrder&accesskey=" + _config.Key + $"&id={id}&currency=hsr_cnyt";
                path += body;
                var req = BuildRequest(path, "GET", body);
                RestUtil.LogRestRequest(req);
                var response = _restClient.Execute(req);
                if (response == null || response.StatusCode == 0 || response.Content.IndexOf("total_amount") < 0)
                {
                    Log.Debug($"Hpx GetOrderState response is null or failed ...");
                    return null;
                }
                JObject j = JObject.Parse(response.Content);
                j = JObject.Parse(j["data"].ToString());
                OrderStateReply reply = j.ToObject<OrderStateReply>();
                Log.Debug("Hpx GetOrderState End");
                return reply;
            }
            catch (Exception ex)
            {
                Log.Debug($"Hpx GetOrderState Exception:" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tradeType"> 挂单类型 1/0[buy/sell]</param>
        /// <returns></returns>
        public string GetOrdersState(int pageIndex, int tradeType)
        {
            try
            {
                Log.Debug("Hpx GetOrdersState Begin");
                var path = "/api/v2/getOrders?";
                string body = "method=getOrders&accesskey=" + _config.Key + $"&tradeType={tradeType}&currency=hsr_cnyt&pageIndex=1&pageSize=100";
                path += body;
                var req = BuildRequest(path, "GET", body);
                RestUtil.LogRestRequest(req);
                var response = _restClient.Execute(req);
                if (response == null || response.StatusCode == 0)
                {
                    Log.Debug($"Hpx GetOrderState response is null or failed ...");
                    return null;
                }
                Log.Debug("Hpx GetOrdersState End");
                return response.Content;
            }
            catch (Exception ex)
            {
                Log.Debug($"Hpx GetOrdersState Exception:" + ex.Message);
                return null;
            }
        }

        private void Cancel(string orderId)
        {
            try
            {
                Log.Debug("Hpx Cancel Begin");
                var path = "/api/v2/cancel?";
                string body = "method=cancel&accesskey=" + _config.Key + $"&id={orderId}&currency=hsr_cnyt";
                path += body;
                var req = BuildRequest(path, "GET", body);
                RestUtil.LogRestRequest(req);
                var response = _restClient.Execute(req);
                if (response == null || response.StatusCode == 0)
                {
                    Log.Debug($"Hpx Cancel response is null or failed ...");
                    Cancel(orderId);
                }
                Log.Debug("Hpx Cancel End");
            }
            catch (Exception ex)
            {
                Log.Debug($"Hpx Cancel Exception:" + ex.Message);
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

        private string HttpPost(string Url, string postDataStr)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Url);
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.ContentLength = Encoding.UTF8.GetByteCount(postDataStr);
                Stream myRequestStream = request.GetRequestStream();
                StreamWriter myStreamWriter = new StreamWriter(myRequestStream, Encoding.GetEncoding("gb2312"));
                myStreamWriter.Write(postDataStr);
                myStreamWriter.Close();

                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                Stream myResponseStream = response.GetResponseStream();
                StreamReader myStreamReader = new StreamReader(myResponseStream, Encoding.GetEncoding("utf-8"));
                string retString = myStreamReader.ReadToEnd();
                myStreamReader.Close();
                myResponseStream.Close();
                return retString;
            }
            catch(Exception ex)
            {
                Log.Info("HttpPost Failed:"+ex.Message);
                Log.Debug("HttpPost Failed:" + ex.Message);
                return "";
            }
        }
    }
}