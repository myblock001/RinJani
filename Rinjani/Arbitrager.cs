using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using static System.Threading.Thread;
using Rinjani.Properties;
using Newtonsoft.Json.Linq;
using Rinjani.Hpx;

namespace Rinjani
{
    public class Arbitrager : IArbitrager
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly List<Order> _activeOrders = new List<Order>();
        private readonly IBrokerAdapterRouter _brokerAdapterRouter;
        private readonly IConfigStore _configStore;
        private readonly IBalanceService _positionService;
        private readonly IQuoteAggregator _quoteAggregator;
        private readonly BrokerConfig _configHpx;
        private readonly BrokerConfig _configZb;
        private readonly CSVFileHelper csvFileHelper = new CSVFileHelper();

        public Arbitrager(IQuoteAggregator quoteAggregator,
            IConfigStore configStore,
            IBalanceService positionService,
            IBrokerAdapterRouter brokerAdapterRouter)
        {
            _quoteAggregator = quoteAggregator ?? throw new ArgumentNullException(nameof(quoteAggregator));
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _brokerAdapterRouter = brokerAdapterRouter ?? throw new ArgumentNullException(nameof(brokerAdapterRouter));
            _positionService = positionService ?? throw new ArgumentNullException(nameof(positionService));
            _configHpx = configStore.Config.Brokers.First(b => b.Broker == Broker.Hpx);
            _configZb = configStore.Config.Brokers.First(b => b.Broker == Broker.Zb);
        }

        public void Start()
        {
            Log.Info(Resources.StartingArbitrager, nameof(Arbitrager));
            _quoteAggregator.QuoteUpdated += QuoteUpdated;
            Log.Info(Resources.StartedArbitrager, nameof(Arbitrager));
        }

        public void Dispose()
        {
            _positionService?.Dispose();
            _quoteAggregator?.Dispose();
        }

        void HpxBuyOrderDeal()
        {
            var config = _configStore.Config;
            var bestAskHpx = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Hpx)
                .OrderBy(q => q.Price).FirstOrDefault();
            if (bestAskHpx == null)
            {
                return;
            }

            ///Hpx卖价低于Zb基准价
            var bestBidZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Zb)
            .OrderByDescending(q => q.Price).FirstOrDefault();
            if (bestBidZb == null)
            {
                return;
            }
            decimal price = Math.Max(bestBidZb.Price, bestBidZb.BasePrice)*_configZb.Leg2ExRate - 0.01m;
            decimal invertedSpread = price - bestAskHpx.Price * _configHpx.Leg2ExRate;
            decimal availableVolume = Util.RoundDown(Math.Min(bestBidZb.Volume, bestAskHpx.Volume), 3);
            var balanceMap = _positionService.BalanceMap;
            decimal allowedSizeHpx = balanceMap[bestAskHpx.Broker].Leg2 / bestAskHpx.Price;
            decimal allowedSizeZb = balanceMap[bestBidZb.Broker].Leg1;
            decimal targetVolume = new[] { availableVolume, config.MaxSize, allowedSizeHpx, allowedSizeZb }.Min();
            if (targetVolume < config.MinSize)
                return;
            targetVolume = Util.RoundDown(targetVolume, 2);
            if (invertedSpread / price > config.ArbitragePoint / 100)
            {
                SpreadAnalysisResult result = new SpreadAnalysisResult
                {
                    BestOrderHpx = new Quote(Broker.Hpx, QuoteSide.Bid, bestAskHpx.Price, bestAskHpx.BasePrice, bestAskHpx.Volume),
                    BestOrderZb = new Quote(Broker.Zb, QuoteSide.Ask, price, bestBidZb.BasePrice, bestBidZb.Volume),
                    InvertedSpread = invertedSpread,
                    AvailableVolume = availableVolume,
                    TargetVolume = targetVolume,
                };
                HpxExecuteOrder(result);
                return;
            }
        }

        void HpxSellOrderDeal()
        {
            var config = _configStore.Config;
            var bestBidHpx = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Hpx)
                .OrderByDescending(q => q.Price).FirstOrDefault();
            if (bestBidHpx == null)
            {
                return;
            }
            ///Hpx买价高于Zb基准价
            var bestAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
            .OrderBy(q => q.Price).FirstOrDefault();
            if (bestAskZb == null)
            {
                return;
            }
            decimal price = Math.Min(bestAskZb.Price, bestAskZb.BasePrice)*_configZb.Leg2ExRate + 0.01m;
            decimal invertedSpread = bestBidHpx.Price * _configHpx.Leg2ExRate - price;

            decimal availableVolume = Util.RoundDown(Math.Min(bestAskZb.Volume, bestBidHpx.Volume), 3);

            var balanceMap = _positionService.BalanceMap;
            decimal allowedSizeHpx = balanceMap[bestBidHpx.Broker].Leg1;
            decimal allowedSizeZb = balanceMap[bestAskZb.Broker].Leg2 / bestAskZb.Price;
            decimal targetVolume = new[] { availableVolume, config.MaxSize, allowedSizeHpx, allowedSizeZb }.Min();
            targetVolume = Util.RoundDown(targetVolume, 2);
            if (targetVolume < config.MinSize)
                return;
            if (invertedSpread / price > config.ArbitragePoint / 100)
            {
                SpreadAnalysisResult result = new SpreadAnalysisResult
                {
                    BestOrderHpx = new Quote(Broker.Hpx, QuoteSide.Ask, bestBidHpx.Price, bestBidHpx.BasePrice, bestBidHpx.Volume),
                    BestOrderZb = new Quote(Broker.Zb, QuoteSide.Bid, price, bestAskZb.BasePrice, bestAskZb.Volume),
                    InvertedSpread = invertedSpread,
                    AvailableVolume = availableVolume,
                    TargetVolume = targetVolume,
                };
                HpxExecuteOrder(result);
                return;
            }
        }

        void ZbSellOrderDeal()
        {
            var config = _configStore.Config;
            ///Hpx卖价低于Zb基准价
            var bestBidZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Zb)
            .OrderByDescending(q => q.Price).FirstOrDefault();
            if (bestBidZb == null)
            {
                return;
            }
            decimal price = Math.Max(bestBidZb.Price, bestBidZb.BasePrice) - 0.01m;
            SpreadAnalysisResult result = new SpreadAnalysisResult
            {
                BestOrderZb = new Quote(Broker.Zb, QuoteSide.Ask, price, bestBidZb.BasePrice, _activeOrders[_activeOrders.Count - 1].FilledSize),
            };
            ZbExecuteOrder(result);
            return;
        }

        void ZbBuyOrderDeal()
        {
            var config = _configStore.Config;
            ///Hpx卖价低于Zb基准价
            var bestAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
            .OrderBy(q => q.Price).FirstOrDefault();
            if (bestAskZb == null)
            {
                return;
            }
            decimal price = Math.Min(bestAskZb.Price, bestAskZb.BasePrice) + 0.01m;
            SpreadAnalysisResult result = new SpreadAnalysisResult
            {
                BestOrderZb = new Quote(Broker.Zb, QuoteSide.Bid, price, bestAskZb.BasePrice, _activeOrders[_activeOrders.Count - 1].FilledSize),
            };
            ZbExecuteOrder(result);
            return;
        }

        private void HpxExecuteOrder(SpreadAnalysisResult result)
        {
            var config = _configStore.Config;
            var bestOrderHpx = result.BestOrderHpx;
            var invertedSpread = result.InvertedSpread;
            var availableVolume = result.AvailableVolume;
            var targetVolume = result.TargetVolume;
            var targetProfit = result.TargetProfit;

            if (invertedSpread <= 0)
            {
                Log.Info(Resources.NoArbitrageOpportunitySpreadIsNotInverted);
                return;
            }

            Log.Info(Resources.FoundInvertedQuotes);
            if (availableVolume < config.MinSize)
            {
                Log.Info(Resources.AvailableVolumeIsSmallerThanMinSize);
                return;
            }

            if (config.DemoMode)
            {
                Log.Info(Resources.ThisIsDemoModeNotSendingOrders);
                return;
            }

            Log.Info(string.Format("套利方向 Broker1 {0}", bestOrderHpx.Side == QuoteSide.Ask ? "卖单" : "买单"));
            Log.Info(string.Format("Broker1价格 {0},  Broker2基准价 {1}", bestOrderHpx.Price, result.BestOrderZb.BasePrice));
            Log.Info(string.Format("套利点{0}%,差异点{1}%", config.ArbitragePoint, (100 * invertedSpread / result.BestOrderZb.BasePrice).ToString("0.00")));

            Log.Info(Resources.SendingOrderTargettingQuote, bestOrderHpx);
            SendOrder(bestOrderHpx, targetVolume, OrderType.Limit);
            if (_activeOrders[0].BrokerOrderId == "0x3fffff")
            {
                Log.Info("Hpx余额不足");
                _activeOrders.Clear();
                return;
            }
            if (_activeOrders[0].BrokerOrderId == null)
            {
                Sleep(config.SleepAfterSend);
                _activeOrders.Clear();
                return;
            }
            Sleep(config.SleepAfterSend);
            exeTimes = 0;
            HpxCheckOrderState();
        }

        int exeTimes = 0;
        private void HpxCheckOrderState()
        {
            if (_activeOrders.Count == 0)
                return;
            exeTimes++;
            var order = _activeOrders[0];
            var config = _configStore.Config;
            try
            {
                _brokerAdapterRouter.Refresh(order);
            }
            catch (Exception ex)
            {
                Log.Warn(ex.Message);
                Log.Debug(ex);
            }

            if (order.Status != OrderStatus.Filled)
            {
                if (order.Side == OrderSide.Buy)
                    Log.Warn(Resources.BuyLegIsNotFilledYetPendingSizeIs, order.PendingSize);
                else
                    Log.Warn(Resources.SellLegIsNotFilledYetPendingSizeIs, order.PendingSize);

                if (order.FilledSize < config.MinSize)
                {
                    if (order.Status == OrderStatus.Canceled)
                        return;
                    if (exeTimes < 2)
                    {
                        HpxCheckOrderState();
                        return;
                    }
                    _brokerAdapterRouter.Cancel(order);
                    Log.Info("Hpx 删除套利订单...");
                    Sleep(config.SleepAfterSend);
                    _brokerAdapterRouter.Refresh(order);//防止撤销时部分成交
                    if(order.Status!=OrderStatus.Canceled)
                    {
                        Sleep(config.SleepAfterSend);
                        HpxCheckOrderState();
                        exeTimes = 1;
                        return;
                    }
                    if (order.FilledSize > config.MinSize)
                    {
                        HpxCheckOrderState();
                        return;
                    }
                    Sleep(config.SleepAfterSend);
                    _activeOrders.Clear();
                    return;
                }
                else
                {
                    order.Size = order.FilledSize;
                    order.Status = OrderStatus.Filled;
                }
            }

            if (order.Status == OrderStatus.Filled)
            {
                Log.Info("Hpx order Fill price is {0},Fill volume is {1}", order.Price, order.Size);
                return;
            }
        }

        private void ZbExecuteOrder(SpreadAnalysisResult result)
        {
            var config = _configStore.Config;
            var bestOrderZb = result.BestOrderZb;

            Log.Info($"Zb {bestOrderZb.Side.ToString()}  {bestOrderZb.Price}  {bestOrderZb.Volume}");

            if (config.DemoMode)
            {
                Log.Info(Resources.ThisIsDemoModeNotSendingOrders);
                return;
            }
            Log.Info(Resources.SendingOrderTargettingQuote, bestOrderZb);
            SendOrder(bestOrderZb, bestOrderZb.Volume, OrderType.Limit);
            if (_activeOrders[_activeOrders.Count - 1].BrokerOrderId == "0x3fffff")
            {
                Log.Info("Zb余额不足");
                _activeOrders.RemoveAt(_activeOrders.Count - 1);
            }
            while (_activeOrders.Count <= 1 || _activeOrders[_activeOrders.Count - 1].BrokerOrderId == null)
            {
                Sleep(config.SleepAfterSend);
                Log.Info("Zb Order failure,Re-order", bestOrderZb);
                ZbExecuteOrder(result);
            }
            Sleep(config.SleepAfterSend);
        }

        private decimal ZbFilledSize = 0;
        private decimal ZbAverageFilledPrice = 0;
        private void ZbCheckOrderState()
        {
            if (_activeOrders.Count < 2)
                return;
            var config = _configStore.Config;
            foreach (var i in Enumerable.Range(1, config.MaxRetryCount))
            {
                var order = _activeOrders[_activeOrders.Count - 1];
                Log.Info(Resources.OrderCheckAttempt, i);
                Log.Info(Resources.CheckingIfBothLegsAreDoneOrNot);

                try
                {
                    _brokerAdapterRouter.Refresh(order);
                }
                catch (Exception ex)
                {
                    Log.Warn(ex.Message);
                    Log.Debug(ex);
                }

                if (order.Status != OrderStatus.Filled)
                {
                    Log.Warn("Zb Leg is not filled yet,pending size is {0}", order.PendingSize);
                    ZbFilledSize = 0;//第一个order为hpx下单
                    for (int j = 1; j < _activeOrders.Count; j++)
                    {
                        ZbFilledSize += _activeOrders[j].FilledSize;
                    }
                    if (ZbFilledSize>0 && ZbFilledSize >= _activeOrders[0].FilledSize-0.001m)
                    {
                        _brokerAdapterRouter.Cancel(order);
                        order.Status = OrderStatus.Filled;
                    }
                    else
                    {
                        _quoteAggregator.Aggregate();//更新ticker数据
                        if (order.Side == OrderSide.Buy)
                        {
                            var bestAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
                                .OrderBy(q => q.Price).FirstOrDefault();
                            if (bestAskZb == null)
                            {
                                continue;
                            }
                            decimal price = Math.Min(bestAskZb.Price, bestAskZb.BasePrice);
                            if (order.Price < price)
                            {
                                Log.Info($"Zb买单价格为{order.Price},小于基准价{price},重新下单");
                                _brokerAdapterRouter.Cancel(order);
                                Sleep(config.SleepAfterSend);
                                _brokerAdapterRouter.Refresh(order);//防止撤销时部分成交
                                for (int j = 1; j < _activeOrders.Count; j++)
                                {
                                    ZbFilledSize += _activeOrders[j].FilledSize;
                                }
                                if (ZbFilledSize > 0 && ZbFilledSize >= _activeOrders[0].FilledSize - 0.001m)
                                {
                                    _brokerAdapterRouter.Cancel(order);
                                    Sleep(config.SleepAfterSend);
                                    order.Status = OrderStatus.Filled;
                                    Log.Info("Zb 订单成交...");
                                    continue;
                                }
                                order.Status = OrderStatus.Filled;
                                order.Size = order.FilledSize;
                                if (order.FilledSize < config.MinSize)
                                    _activeOrders.Remove(order);
                                SpreadAnalysisResult result = new SpreadAnalysisResult
                                {
                                    BestOrderZb = new Quote(Broker.Zb, QuoteSide.Bid, price, bestAskZb.BasePrice, _activeOrders[0].FilledSize - ZbFilledSize),
                                };
                                if (result.BestOrderZb.Volume < config.MinSize)
                                    return;
                                ZbExecuteOrder(result);
                                continue;
                            }
                        }
                        else
                        {
                            var bestBidZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Zb)
                                .OrderByDescending(q => q.Price).FirstOrDefault();
                            if (bestBidZb == null)
                            {
                                continue;
                            }
                            decimal price = Math.Max(bestBidZb.Price, bestBidZb.BasePrice);
                            if (order.Price > price)
                            {
                                Log.Info($"Zb卖单价格为{order.Price},大于基准价{price},重新下单");
                                _brokerAdapterRouter.Cancel(order);
                                Sleep(config.SleepAfterSend);
                                _brokerAdapterRouter.Refresh(order);//防止撤销时部分成交
                                for (int j = 1; j < _activeOrders.Count; j++)
                                {
                                    ZbFilledSize += _activeOrders[j].FilledSize;
                                }
                                if (ZbFilledSize > 0 && ZbFilledSize >= _activeOrders[0].FilledSize - 0.001m)
                                {
                                    _brokerAdapterRouter.Cancel(order);
                                    Sleep(config.SleepAfterSend);
                                    order.Status = OrderStatus.Filled;
                                    Log.Info("Zb 订单成交...");
                                    continue;
                                }
                                order.Status = OrderStatus.Filled;
                                order.Size = order.FilledSize;
                                if (order.FilledSize < config.MinSize)
                                    _activeOrders.Remove(order);
                                SpreadAnalysisResult result = new SpreadAnalysisResult
                                {
                                    BestOrderZb = new Quote(Broker.Zb, QuoteSide.Ask, price, bestBidZb.BasePrice, _activeOrders[0].FilledSize - ZbFilledSize),
                                };
                                if (result.BestOrderZb.Volume < config.MinSize)
                                    return;
                                ZbExecuteOrder(result);
                                continue;
                            }
                        }
                    }
                }

                if (order.Status == OrderStatus.Filled)
                {
                    decimal _spendCash = 0;
                    ZbFilledSize = 0;
                    for (int j = 1; j < _activeOrders.Count; j++)
                    {
                        ZbFilledSize += _activeOrders[j].FilledSize;
                        _spendCash += _activeOrders[j].FilledSize * _activeOrders[j].Price;
                    }
                    ZbAverageFilledPrice = _spendCash / ZbFilledSize;
                    decimal profit = 0;
                    if (order.Side == OrderSide.Buy)
                    {
                        profit = Math.Round(_activeOrders[0].FilledSize * _activeOrders[0].Price - _spendCash);
                        Log.Info(Resources.BuyFillPriceIs, ZbAverageFilledPrice);
                        Log.Info(Resources.SellFillPriceIs, _activeOrders[0].Price);
                    }
                    else
                    {
                        profit = Math.Round(_spendCash - _activeOrders[0].FilledSize * _activeOrders[0].Price);
                        Log.Info(Resources.SellFillPriceIs, ZbAverageFilledPrice);
                        Log.Info(Resources.BuyFillPriceIs, ZbAverageFilledPrice);
                    }
                    Log.Info(Resources.BothLegsAreSuccessfullyFilled);
                    Log.Info(Resources.ProfitIs, profit);
                    csvFileHelper.UpdateCSVFile(_activeOrders);
                    _activeOrders.Clear();
                    break;
                }

                if (i == config.MaxRetryCount)
                {
                    Log.Warn(Resources.MaxRetryCountReachedCancellingThePendingOrders);
                    if (order.Status != OrderStatus.Filled)
                    {
                        _brokerAdapterRouter.Cancel(order);
                    }
                    break;
                }
                Sleep(config.OrderStatusCheckInterval);
            }
        }

        private void QuoteUpdated(object sender, EventArgs e)
        {
            try
            {
                Log.Info(Util.Hr(20) + "ARBITRAGER" + Util.Hr(20));
                Arbitrage();
                Log.Info(Util.Hr(50));
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
                Log.Debug(ex);
                EmailHelper.SendMailUse(_configStore.Config.EmailAddress,"Rinjnai程序退出", Resources.ArbitragerThreadHasBeenStopped+ex.Message);
                if (Environment.UserInteractive)
                {
                    Log.Error(Resources.ArbitragerThreadHasBeenStopped);
                    _positionService.Dispose();
                    Console.ReadLine();
                }
                Environment.Exit(-1);
            }
        }

        private void SendOrder(Quote quote, decimal targetVolume, OrderType orderType)
        {
            var brokerConfig = _configStore.Config.Brokers.First(x => x.Broker == quote.Broker);
            var orderSide = quote.Side == QuoteSide.Ask ? OrderSide.Sell : OrderSide.Buy;
            var order = new Order(quote.Broker, orderSide, targetVolume, quote.Price, orderType);
            _brokerAdapterRouter.Send(order);
            _activeOrders.Add(order);
        }

        private void Arbitrage()
        {
            InitHpxOrder();
            //套利功能
            if (_configStore.Config.Arbitrage)
            {
                Log.Info(Resources.LookingForOpportunity);
                if (_positionService.BalanceMap.Count < 2)
                {
                    _positionService.GetBalances();
                    return;
                }

                if (_positionService.BalanceMap[Broker.Hpx] == null || _positionService.BalanceMap[Broker.Zb] == null)
                {
                    _positionService.GetBalances();
                    return;
                }
                if (_activeOrders.Count == 0)
                {
                    ZbFilledSize = 0;
                    HpxBuyOrderDeal();
                }
                if (_activeOrders.Count == 0)
                {
                    ZbFilledSize = 0;
                    HpxSellOrderDeal();
                }
                if (_activeOrders.Count >= 1)
                {
                    if (_activeOrders[0].Side == OrderSide.Buy)
                        ZbSellOrderDeal();
                    else
                        ZbBuyOrderDeal();
                    ZbCheckOrderState();
                    _positionService.GetBalances();
                }
                _activeOrders.Clear();
            }

            //流动性机器人功能
            if (_configStore.Config.LiquidBot)
            {
                if (_configStore.Config.CancelAllOrders)
                {
                    GetOrdersState(1, 0, Broker.Hpx);
                    Sleep(_configStore.Config.SleepAfterSend);
                    foreach (var s in ordersState)
                    {
                        Order o = new Order();
                        o.BrokerOrderId = s.id;
                        o.Broker = Broker.Hpx;
                        s.SetOrder(o);
                        Log.Info($"删除订单{o.BrokerOrderId}  {o.Price}  {o.Size}...");
                        _brokerAdapterRouter.Cancel(o);
                        Sleep(1000);
                    }
                    Environment.Exit(-1);
                }

                LiquidBot();
            }
            MonitorBalance();
        }

        public void MonitorBalance()
        {
            _positionService.GetBalances();
            var config = _configStore.Config;
            if (_positionService.BalanceMap[Broker.Hpx] == null || _positionService.BalanceMap[Broker.Zb] == null)
                return;
            if (_positionService.BalanceMap[Broker.Hpx].Leg1 < _configHpx.Leg1ThresholdSendEmail|| _positionService.BalanceMap[Broker.Hpx].Leg2 < _configHpx.Leg2ThresholdSendEmail)
            {
                string content = $"Hpx Leg1 {_positionService.BalanceMap[Broker.Hpx].Leg1},Leg2 {_positionService.BalanceMap[Broker.Hpx].Leg2}";
                EmailHelper.SendMailUse(_configStore.Config.EmailAddress, "Hpx 余额不足", content);
            }
            if (_positionService.BalanceMap[Broker.Zb].Leg1 < _configZb.Leg1ThresholdSendEmail || _positionService.BalanceMap[Broker.Zb].Leg2 < _configZb.Leg2ThresholdSendEmail)
            {
                string content = $"Zb Leg1 {_positionService.BalanceMap[Broker.Zb].Leg1},Leg2 {_positionService.BalanceMap[Broker.Zb].Leg2}";
                EmailHelper.SendMailUse(_configStore.Config.EmailAddress, "Zb 余额不足", content);
            }
        }

        public List<OrderStateReply> ordersState;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tradeType">0/1[buy/sell]</param>
        /// <param name="broker"></param>
        public void GetOrdersState(int pageIndex, int tradeType, Broker broker)
        {
            try
            {
                Log.Debug($"GetOrdersState Start");
                var brokerConfig = _configStore.Config.Brokers.First(x => x.Broker == Broker.Hpx);
                string response = _brokerAdapterRouter.GetOrdersState(pageIndex, tradeType, broker);
                JObject j = JObject.Parse(response);
                JArray ja = JArray.Parse(j["data"].ToString());
                ordersState = ja.ToObject<List<OrderStateReply>>();
                Log.Debug($"GetOrdersState Stop");
            }
            catch (Exception ex)
            {
                Log.Debug($"GetOrdersState:{ex.Message}");
                //Log.Info($"GetOrdersState:{ex.Message}");
                ordersState = null;
            }
        }

        public void InitHpxOrder()
        {
            if (startflag)
            {
                //startflag = false;
                //allBuyOrderHpx.Clear();
                //allSellOrderHpx.Clear();
                //GetOrdersState(1, 0, Broker.Hpx);
                //if (ordersState == null)
                //    return;
                //foreach (OrderStateReply o in ordersState)
                //{
                //    if (o.type == 0)//buy
                //    {
                //        Order new_order = new Order(Broker.Hpx, OrderSide.Buy, o.total_amount, o.price, OrderType.Limit);
                //        o.SetOrder(new_order);
                //        allBuyOrderHpx.Add(new_order);
                //    }
                //    else//sell
                //    {
                //        Order new_order = new Order(Broker.Hpx, OrderSide.Sell, o.total_amount, o.price, OrderType.Limit);
                //        o.SetOrder(new_order);
                //        allSellOrderHpx.Add(new_order);
                //    }
                //}
                //allBuyOrderHpx.Sort((x, y) => -(x.Price).CompareTo(y.Price));
                //allSellOrderHpx.Sort((x, y) => (x.Price).CompareTo(y.Price));
                
                GetOrdersState(1, 0, Broker.Hpx);
                while (ordersState.Count > 0)
                {
                    Sleep(_configStore.Config.SleepAfterSend);
                    foreach (var s in ordersState)
                    {
                        Order o = new Order();
                        o.BrokerOrderId = s.id;
                        o.Broker = Broker.Hpx;
                        s.SetOrder(o);
                        Log.Info($"删除订单{o.BrokerOrderId}  {o.Price}  {o.Size}...");
                        _brokerAdapterRouter.Cancel(o);
                        Sleep(1000);
                    }
                    GetOrdersState(1, 0, Broker.Hpx);
                }
                startflag = false;
            }
        }

        public void CheckOrdersState(decimal highestBidPrice, decimal lowestAskPrice)
        {
            GetOrdersState(1, 0, Broker.Hpx);
            if (ordersState == null)
                return;
            var config = _configStore.Config;
            var buyOrdersState = ordersState.Where(q => q.type == 0);
            foreach (var orderState in buyOrdersState)
            {
                var order = allBuyOrderHpx.Where(q => orderState.id == q.BrokerOrderId).FirstOrDefault();
                if (order == null)
                {
                    Sleep(config.SleepAfterSend);
                    order = new Order(Broker.Hpx, OrderSide.Buy, orderState.total_amount, orderState.price, OrderType.Limit);
                    orderState.SetOrder(order);
                    allBuyOrderHpx.Add(order);
                    allBuyOrderHpx.Sort((x, y) => -(x.Price).CompareTo(y.Price));
                }
            }
            bool exe_flag = true;
            while (exe_flag)
            {
                exe_flag = false;
                foreach (var order in allBuyOrderHpx)
                {
                    var orderState = ordersState.Where(q => order.BrokerOrderId == q.id).FirstOrDefault();
                    decimal lastPendingSize = order.PendingSize;
                    if (orderState == null)
                    {
                        Sleep(config.SleepAfterSend);
                        _brokerAdapterRouter.Refresh(order);
                        if (lastPendingSize == order.PendingSize)
                        {
                            allBuyOrderHpx.Remove(order);
                            PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                            if (allBuyOrderHpx.Count == 0)
                                return;
                            exe_flag = true;
                            break;
                        }
                    }
                    else
                        orderState.SetOrder(order);
                    decimal curFilledSize = lastPendingSize - order.PendingSize;
                    if (curFilledSize >= config.MinSize)
                    {
                        Order new_order = new Order(order.Broker, order.Side, curFilledSize, order.Price,
                        order.Type);
                        new_order.Size = curFilledSize;
                        new_order.FilledSize = curFilledSize;
                        new_order.BrokerOrderId = order.BrokerOrderId;
                        _activeOrders.Add(new_order);
                        Log.Info($"Hpx订单部分成交，成交价格{new_order.Price},成交数量{new_order.FilledSize},Zb开始下单");
                        ZbSellLiquidOrderDeal();
                        ZbCheckLiquidOrderState();
                        allBuyOrderHpx.Remove(order);
                        PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                        if (allBuyOrderHpx.Count == 0)
                            return;
                        Sleep(config.SleepAfterSend);
                        GetOrdersState(1, 0, Broker.Hpx);
                        _activeOrders.Clear();
                        exe_flag = true;
                        break;
                    }
                    if (order.Price > highestBidPrice)
                    {
                        Log.Info($"Hpx买单价格为{order.Price},不符合条件，删除");
                        _brokerAdapterRouter.Cancel(order);
                        allBuyOrderHpx.Remove(order);
                        PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                        if (allBuyOrderHpx.Count == 0)
                            return;
                        exe_flag = true;
                        Sleep(config.SleepAfterSend);
                        break;
                    }
                }
            }

            GetOrdersState(1, 1, Broker.Hpx);
            if (ordersState == null)
                return;
            var sellOrdersState = ordersState.Where(q => q.type == 1);
            foreach (var orderState in sellOrdersState)
            {
                var order = allSellOrderHpx.Where(q => orderState.id == q.BrokerOrderId).FirstOrDefault();
                if (order == null)
                {
                    Sleep(config.SleepAfterSend);
                    order = new Order(Broker.Hpx, OrderSide.Sell, orderState.total_amount, orderState.price, OrderType.Limit);
                    orderState.SetOrder(order);
                    allSellOrderHpx.Add(order);
                    allSellOrderHpx.Sort((x, y) => (x.Price).CompareTo(y.Price));
                }
            }
            exe_flag = true;
            while (exe_flag)
            {
                exe_flag = false;
                foreach (var order in allSellOrderHpx)
                {
                    var orderState = ordersState.Where(q => order.BrokerOrderId == q.id).FirstOrDefault();
                    decimal lastPendingSize = order.PendingSize;
                    if (orderState == null)
                    {
                        Sleep(config.SleepAfterSend);
                        _brokerAdapterRouter.Refresh(order);
                        if (lastPendingSize == order.PendingSize)
                        {
                            allSellOrderHpx.Remove(order);
                            PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                            if (allSellOrderHpx.Count == 0)
                                return;
                            exe_flag = true;
                            break;
                        }
                    }
                    else
                        orderState.SetOrder(order);
                    decimal curFilledSize = lastPendingSize - order.PendingSize;
                    if (curFilledSize >= config.MinSize)
                    {
                        Order new_order = new Order(order.Broker, order.Side, order.Size, order.Price,
                        order.Type);
                        new_order.Size = curFilledSize;
                        new_order.FilledSize = curFilledSize;
                        new_order.BrokerOrderId = order.BrokerOrderId;
                        _activeOrders.Add(new_order);
                        Log.Info($"Hpx订单部分成交，成交价格{new_order.Price},成交数量{new_order.FilledSize},Zb开始下单");
                        ZbBuyLiquidOrderDeal();
                        ZbCheckLiquidOrderState();
                        allSellOrderHpx.Remove(order);
                        PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                        if (allSellOrderHpx.Count == 0)
                            return;
                        Sleep(config.SleepAfterSend);
                        GetOrdersState(1, 1, Broker.Hpx);
                        _activeOrders.Clear();
                        exe_flag = true;
                        if (ordersState == null)
                            return;
                        break;
                    }
                    if (order.Price < lowestAskPrice)
                    {
                        Log.Info($"Hpx卖单价格为{order.Price},不符合条件，删除");
                        _brokerAdapterRouter.Cancel(order);
                        allSellOrderHpx.Remove(order);
                        PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                        if (allSellOrderHpx.Count == 0)
                            return;
                        exe_flag = true;
                        Sleep(config.SleepAfterSend);
                        break;
                    }
                }
            }
        }

        void ZbSellLiquidOrderDeal()
        {
            var config = _configStore.Config;
            ///Hpx卖价低于Zb基准价
            var bestBidZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Zb)
            .OrderByDescending(q => q.Price).FirstOrDefault();
            if (bestBidZb == null)
            {
                return;
            }
            decimal price = Math.Min(bestBidZb.Price, bestBidZb.BasePrice) - 0.01m;
            SpreadAnalysisResult result = new SpreadAnalysisResult
            {
                BestOrderZb = new Quote(Broker.Zb, QuoteSide.Ask, price, bestBidZb.BasePrice, _activeOrders[_activeOrders.Count - 1].FilledSize),
            };
            ZbExecuteOrder(result);
            return;
        }

        void ZbBuyLiquidOrderDeal()
        {
            var config = _configStore.Config;
            ///Hpx卖价低于Zb基准价
            var bestAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
            .OrderBy(q => q.Price).FirstOrDefault();
            if (bestAskZb == null)
            {
                return;
            }
            decimal price = Math.Max(bestAskZb.Price, bestAskZb.BasePrice) + 0.01m;
            SpreadAnalysisResult result = new SpreadAnalysisResult
            {
                BestOrderZb = new Quote(Broker.Zb, QuoteSide.Bid, price, bestAskZb.BasePrice, _activeOrders[_activeOrders.Count - 1].FilledSize),
            };
            ZbExecuteOrder(result);
            return;
        }

        private void ZbCheckLiquidOrderState()
        {
            if (_activeOrders.Count < 2)
                return;
            var config = _configStore.Config;
            foreach (var i in Enumerable.Range(1, config.MaxRetryCount))
            {
                var order = _activeOrders[_activeOrders.Count - 1];
                Log.Info(Resources.OrderCheckAttempt, i);
                Log.Info(Resources.CheckingIfBothLegsAreDoneOrNot);

                try
                {
                    _brokerAdapterRouter.Refresh(order);
                }
                catch (Exception ex)
                {
                    Log.Warn(ex.Message);
                    Log.Debug(ex);
                }

                if (order.Status != OrderStatus.Filled)
                {
                    Log.Warn("Zb Leg is not filled yet,pending size is {0}", order.PendingSize);
                    ZbFilledSize = 0;//第一个order为hpx下单
                    for (int j = 1; j < _activeOrders.Count; j++)
                    {
                        ZbFilledSize += _activeOrders[j].FilledSize;
                    }
                    if (ZbFilledSize > 0 && ZbFilledSize >= _activeOrders[0].FilledSize - 0.001m)
                    {
                        _brokerAdapterRouter.Cancel(order);
                        Sleep(config.SleepAfterSend);
                        order.Status = OrderStatus.Filled;
                    }
                    else
                    {
                        _quoteAggregator.Aggregate();//更新ticker数据
                        if (order.Side == OrderSide.Buy)
                        {
                            var bestAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
                                .OrderBy(q => q.Price).FirstOrDefault();
                            if (bestAskZb == null)
                            {
                                continue;
                            }
                            decimal price = Math.Max(bestAskZb.Price, bestAskZb.BasePrice);
                            if (order.Price < price)
                            {
                                Log.Info($"Zb买单价格为{order.Price},小于基准价{price},重新下单");
                                decimal lastPendingSize = order.PendingSize;
                                _brokerAdapterRouter.Cancel(order);
                                Sleep(config.SleepAfterSend);
                                _brokerAdapterRouter.Refresh(order);//防止撤销时部分成交
                                for (int j = 1; j < _activeOrders.Count; j++)
                                {
                                    ZbFilledSize += _activeOrders[j].FilledSize;
                                }
                                if (ZbFilledSize > 0 && ZbFilledSize >= _activeOrders[0].FilledSize - 0.001m)
                                {
                                    _brokerAdapterRouter.Cancel(order);
                                    Sleep(config.SleepAfterSend);
                                    order.Status = OrderStatus.Filled;
                                    continue;
                                }
                                Sleep(config.SleepAfterSend);
                                order.Status = OrderStatus.Filled;
                                order.Size = order.FilledSize;
                                if (order.FilledSize < config.MinSize)
                                    _activeOrders.Remove(order);
                                SpreadAnalysisResult result = new SpreadAnalysisResult
                                {
                                    BestOrderZb = new Quote(Broker.Zb, QuoteSide.Bid, price, bestAskZb.BasePrice, _activeOrders[0].FilledSize - ZbFilledSize),
                                };
                                if (result.BestOrderZb.Volume < config.MinSize)
                                    return;
                                ZbExecuteOrder(result);
                                continue;
                            }
                        }
                        else
                        {
                            var bestBidZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Zb)
                                .OrderByDescending(q => q.Price).FirstOrDefault();
                            if (bestBidZb == null)
                            {
                                continue;
                            }
                            decimal price = Math.Min(bestBidZb.Price, bestBidZb.BasePrice);
                            if (order.Price > price)
                            {
                                Log.Info($"Zb卖单价格为{order.Price},大于基准价{price},重新下单");
                                _brokerAdapterRouter.Cancel(order);
                                Sleep(config.SleepAfterSend);
                                _brokerAdapterRouter.Refresh(order);//防止撤销时部分成交
                                for (int j = 1; j < _activeOrders.Count; j++)
                                {
                                    ZbFilledSize += _activeOrders[j].FilledSize;
                                }
                                if (ZbFilledSize > 0 && ZbFilledSize >= _activeOrders[0].FilledSize - 0.001m)
                                {
                                    _brokerAdapterRouter.Cancel(order);
                                    Sleep(config.SleepAfterSend);
                                    order.Status = OrderStatus.Filled;
                                    continue;
                                }
                                order.Status = OrderStatus.Filled;
                                order.Size = order.FilledSize;
                                if (order.FilledSize < config.MinSize)
                                    _activeOrders.Remove(order);
                                SpreadAnalysisResult result = new SpreadAnalysisResult
                                {
                                    BestOrderZb = new Quote(Broker.Zb, QuoteSide.Ask, price, bestBidZb.BasePrice, _activeOrders[0].FilledSize - ZbFilledSize),
                                };
                                if (result.BestOrderZb.Volume < config.MinSize)
                                    return;
                                ZbExecuteOrder(result);
                                continue;
                            }
                        }
                    }
                }

                if (order.Status == OrderStatus.Filled)
                {
                    decimal _spendCash = 0;
                    ZbFilledSize = 0;
                    for (int j = 1; j < _activeOrders.Count; j++)
                    {
                        ZbFilledSize += _activeOrders[j].FilledSize;
                        _spendCash += _activeOrders[j].FilledSize * _activeOrders[j].Price;
                    }
                    ZbAverageFilledPrice = _spendCash / ZbFilledSize;
                    decimal profit = 0;
                    if (order.Side == OrderSide.Buy)
                    {
                        profit = Math.Round(_activeOrders[0].FilledSize * _activeOrders[0].Price - _spendCash);
                        Log.Info(Resources.BuyFillPriceIs, ZbAverageFilledPrice);
                        Log.Info(Resources.SellFillPriceIs, _activeOrders[0].Price);
                    }
                    else
                    {
                        profit = Math.Round(_spendCash - _activeOrders[0].FilledSize * _activeOrders[0].Price);
                        Log.Info(Resources.SellFillPriceIs, ZbAverageFilledPrice);
                        Log.Info(Resources.BuyFillPriceIs, ZbAverageFilledPrice);
                    }
                    Log.Info(Resources.BothLegsAreSuccessfullyFilled);
                    Log.Info(Resources.ProfitIs, profit);
                    csvFileHelper.UpdateCSVFile(_activeOrders);
                    _activeOrders.Clear();
                    break;
                }

                if (i == config.MaxRetryCount)
                {
                    Log.Warn(Resources.MaxRetryCountReachedCancellingThePendingOrders);
                    if (order.Status != OrderStatus.Filled)
                    {
                        _brokerAdapterRouter.Cancel(order);
                    }
                    break;
                }
                Sleep(config.OrderStatusCheckInterval);
            }
        }

        private bool startflag = true;
        private List<Order> allBuyOrderHpx = new List<Order>();
        private List<Order> allSellOrderHpx = new List<Order>();
        private void LiquidBot()
        {
            if (ordersState != null)
                ordersState.Clear();
            var config = _configStore.Config;

            var bestBidZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Bid)
                .Where(q => q.Broker == Broker.Zb)
                .OrderByDescending(q => q.Price).FirstOrDefault();
            if (bestBidZb == null)
                return;
            decimal highestBidPrice = bestBidZb.Price * (100 - config.RemovalRatio) / 100;
            var bestAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask)
                .Where(q => q.Broker == Broker.Zb)
                .OrderBy(q => q.Price).FirstOrDefault();
            if (bestAskZb == null)
                return;
            decimal lowestAskPrice = bestAskZb.Price * (100 + config.RemovalRatio) / 100;

            var cpyBidZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Bid)
                .Where(q => q.Broker == Broker.Zb).Where(q => q.Price <= highestBidPrice)
                .OrderByDescending(q => q.Price);
            highestBidPrice = cpyBidZb.FirstOrDefault().Price;
            if (cpyBidZb.Count() == 0)
                return;
            var cpyAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask)
                .Where(q => q.Broker == Broker.Zb).Where(q => q.Price >= lowestAskPrice)
                .OrderBy(q => q.Price);
            lowestAskPrice = cpyAskZb.FirstOrDefault().Price;
            if (cpyAskZb.Count() == 0)
                return;

            if (allBuyOrderHpx.Count == 0)
            {
                foreach (var bid in cpyBidZb)
                {
                    if (allBuyOrderHpx.Count >= config.CopyQuantity)
                        break;
                    decimal cpyVol = bid.Volume * config.VolumeRatio / 100;
                    cpyVol = cpyVol > config.MinSize ? cpyVol : config.MinSize;
                    cpyVol = cpyVol < config.MaxSize ? cpyVol : config.MaxSize;
                    Log.Info($"正在复制买单，当前价格{bid.Price},当前数量{cpyVol}");
                    bid.Broker = Broker.Hpx;
                    bid.Price = bid.Price * _configZb.Leg2ExRate / _configHpx.Leg2ExRate;
                    SendOrder(bid, cpyVol, OrderType.Limit);

                    if (_activeOrders[_activeOrders.Count - 1].BrokerOrderId == null)
                    {
                        _activeOrders.RemoveAt(_activeOrders.Count - 1);
                        if (_activeOrders.Count == 0)
                        {
                            Sleep(config.SleepAfterSend);
                            break;
                        }
                    }
                    else if (_activeOrders[_activeOrders.Count - 1].BrokerOrderId == "0x3fffff")//余额不足
                    {
                        _activeOrders.Clear();
                        Log.Info("Hpx买单余额不足");
                        Sleep(config.SleepAfterSend);
                        break;
                    }
                    allBuyOrderHpx.Add(_activeOrders[_activeOrders.Count - 1]);
                    allBuyOrderHpx.Sort((x, y) => -(x.Price).CompareTo(y.Price));
                    _activeOrders.Clear();
                    Sleep(config.SleepAfterSend);
                }
                PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
            }
            else
            {              
                Sleep(config.SleepAfterSend);
                CheckOrdersState(highestBidPrice, lowestAskPrice);
                Sleep(config.SleepAfterSend);
                if (allBuyOrderHpx.Count == 0)
                    return;
                var bestBuyOrderHpx = allBuyOrderHpx[0];
                var worstBuyOrderHpx = allBuyOrderHpx[allBuyOrderHpx.Count - 1];
                foreach (var bid in cpyBidZb)
                {
                    if (bid.Price <= worstBuyOrderHpx.Price && allBuyOrderHpx.Count >= config.CopyQuantity)
                        continue;

                    var tmp_o = allBuyOrderHpx.Where(q=>q.Price == bid.Price).FirstOrDefault();
                    if (tmp_o != null)
                    {
                        continue;
                    }

                    decimal cpyVol = bid.Volume * config.VolumeRatio / 100;
                    cpyVol = cpyVol > config.MinSize ? cpyVol : config.MinSize;
                    cpyVol = cpyVol < config.MaxSize ? cpyVol : config.MaxSize;
                    Log.Info($"正在复制买单，当前价格{bid.Price},当前数量{cpyVol}");
                    bid.Broker = Broker.Hpx;
                    bid.Price = bid.Price * _configZb.Leg2ExRate / _configHpx.Leg2ExRate;
                    SendOrder(bid, cpyVol, OrderType.Limit);
                    Sleep(config.SleepAfterSend);
                    if (_activeOrders[_activeOrders.Count - 1].BrokerOrderId == "0x3fffff")
                    {
                        Log.Info("Hpx买单余额不足");
                        _brokerAdapterRouter.Cancel(worstBuyOrderHpx);
                        allBuyOrderHpx.Remove(worstBuyOrderHpx);
                        PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                        _activeOrders.Clear();
                        break;
                    }

                    if (_activeOrders[_activeOrders.Count - 1].BrokerOrderId == null)
                    {
                        Log.Info("Hpx下单失败");
                        _activeOrders.Clear();
                        return;
                    }
                    allBuyOrderHpx.Add(_activeOrders[_activeOrders.Count - 1]);
                    allBuyOrderHpx.Sort((x, y) => -(x.Price).CompareTo(y.Price));
                    PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                    _activeOrders.Clear();
                    CheckOrdersState(highestBidPrice, lowestAskPrice);
                    Sleep(config.SleepAfterSend);

                    if (allBuyOrderHpx.Count == 0)
                        return;
                    bestBuyOrderHpx = allBuyOrderHpx[0];
                    worstBuyOrderHpx = allBuyOrderHpx[allBuyOrderHpx.Count - 1];
                    while (allBuyOrderHpx.Count > config.CopyQuantity)
                    {
                        _brokerAdapterRouter.Cancel(worstBuyOrderHpx);
                        allBuyOrderHpx.Remove(worstBuyOrderHpx);
                        if (allBuyOrderHpx.Count == 0)
                            return;
                        worstBuyOrderHpx = allBuyOrderHpx[allBuyOrderHpx.Count - 1];
                        Sleep(config.SleepAfterSend);
                        CheckOrdersState(highestBidPrice, lowestAskPrice);
                        Sleep(config.SleepAfterSend);
                    }
                    PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                }
            }

            if (allSellOrderHpx.Count == 0)
            {
                foreach (var ask in cpyAskZb)
                {
                    if (allSellOrderHpx.Count >= config.CopyQuantity)
                        break;
                    decimal cpyVol = ask.Volume * config.VolumeRatio / 100;
                    cpyVol = cpyVol > config.MinSize ? cpyVol : config.MinSize;
                    cpyVol = cpyVol < config.MaxSize ? cpyVol : config.MaxSize;
                    Log.Info($"正在复制卖单，当前价格{ask.Price},当前数量{cpyVol}");
                    ask.Broker = Broker.Hpx;
                    ask.Price = ask.Price * _configZb.Leg2ExRate / _configHpx.Leg2ExRate;
                    SendOrder(ask, cpyVol, OrderType.Limit);

                    if (_activeOrders[_activeOrders.Count - 1].BrokerOrderId == null)
                    {
                        _activeOrders.RemoveAt(_activeOrders.Count - 1);
                        if (_activeOrders.Count == 0)
                        {
                            Sleep(config.SleepAfterSend);
                            break;
                        }
                    }
                    else if (_activeOrders[_activeOrders.Count - 1].BrokerOrderId == "0x3fffff")//余额不足
                    {
                        _activeOrders.Clear();
                        Log.Info("Hpx卖单余额不足");
                        Sleep(config.SleepAfterSend);
                        break;
                    }
                    allSellOrderHpx.Add(_activeOrders[_activeOrders.Count - 1]);
                    allSellOrderHpx.Sort((x, y) => (x.Price).CompareTo(y.Price));
                    _activeOrders.Clear();
                    Sleep(config.SleepAfterSend);
                }
                PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
            }
            else
            {
                CheckOrdersState(highestBidPrice, lowestAskPrice);
                Sleep(config.SleepAfterSend);
                if (allSellOrderHpx.Count == 0)
                    return;
                Sleep(config.SleepAfterSend);
                var bestSellOrderHpx = allSellOrderHpx[0];
                var worstSellOrderHpx = allSellOrderHpx[allSellOrderHpx.Count - 1];
                foreach (var ask in cpyAskZb)
                {
                    if (ask.Price >= worstSellOrderHpx.Price && allSellOrderHpx.Count >= config.CopyQuantity)
                        continue;
                    var tmp_o = allSellOrderHpx.Where(q => q.Price == ask.Price).FirstOrDefault();
                    if (tmp_o != null)
                    {
                        continue;
                    }

                    decimal cpyVol = ask.Volume * config.VolumeRatio / 100;
                    cpyVol = cpyVol > config.MinSize ? cpyVol : config.MinSize;
                    cpyVol = cpyVol < config.MaxSize ? cpyVol : config.MaxSize;
                    Log.Info($"正在复制卖单，当前价格{ask.Price},当前数量{cpyVol}");
                    ask.Broker = Broker.Hpx;
                    ask.Price = ask.Price * _configZb.Leg2ExRate / _configHpx.Leg2ExRate;
                    SendOrder(ask, cpyVol, OrderType.Limit);
                    Sleep(config.SleepAfterSend);
                    if (_activeOrders[_activeOrders.Count - 1].BrokerOrderId == "0x3fffff")
                    {
                        Log.Info("Hpx卖单余额不足");
                        _brokerAdapterRouter.Cancel(worstSellOrderHpx);
                        allSellOrderHpx.Remove(worstSellOrderHpx);
                        PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                        _activeOrders.Clear();
                        break;
                    }

                    if (_activeOrders[_activeOrders.Count - 1].BrokerOrderId == null)
                    {
                        Log.Info("Hpx下单失败");
                        _activeOrders.Clear();
                        return;
                    }
                    allSellOrderHpx.Add(_activeOrders[_activeOrders.Count - 1]);
                    allSellOrderHpx.Sort((x, y) => (x.Price).CompareTo(y.Price));
                    PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                    _activeOrders.Clear();
                    CheckOrdersState(highestBidPrice, lowestAskPrice);
                    Sleep(config.SleepAfterSend);
                    if (allSellOrderHpx.Count == 0)
                        return;
                    bestSellOrderHpx = allSellOrderHpx[0];
                    worstSellOrderHpx = allSellOrderHpx[allSellOrderHpx.Count - 1];
                    while (allSellOrderHpx.Count > config.CopyQuantity)
                    {
                        _brokerAdapterRouter.Cancel(worstSellOrderHpx);
                        allSellOrderHpx.Remove(worstSellOrderHpx);
                        if (allSellOrderHpx.Count == 0)
                            return;
                        worstSellOrderHpx = allSellOrderHpx[allSellOrderHpx.Count - 1];
                        CheckOrdersState(highestBidPrice, lowestAskPrice);
                        Sleep(config.SleepAfterSend);
                    }
                    PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                }
            }
        }

        void PrintOrderInfo(List<Order> buyOrders, List<Order> sellOrders)
        {
            var cpyBidZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Bid)
                 .Where(q => q.Broker == Broker.Zb).OrderByDescending(q => q.Price).Take(_configStore.Config.CopyQuantity);
            var cpyAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask)
                .Where(q => q.Broker == Broker.Zb).OrderBy(q => q.Price).Take(_configStore.Config.CopyQuantity)
                .OrderByDescending(q => q.Price);

            Log.Info("Zb当前委托卖/买单:");
            int count = 0;
            foreach (Quote o in cpyAskZb)
            {
                Log.Info($"{o.Price}  {o.Volume}");
                if (count++ > _configStore.Config.CopyQuantity)
                {
                    break;
                }
            }
            count = 0;
            decimal basePrice = 0;
            if (_quoteAggregator.Quotes.Count > 0)
                basePrice = _quoteAggregator.Quotes[0].BasePrice;
            Log.Info($"-----基准价 {basePrice}----");
            foreach (Quote o in cpyBidZb)
            {
                Log.Info($"{o.Price}  {o.Volume}");
                if (++count >= _configStore.Config.CopyQuantity)
                    break;
            }

            Log.Info("\r\nHpx当前复制委托卖/买单:");
            foreach (Order o in sellOrders.OrderByDescending(q => q.Price))
            {
                Log.Info($"{o.Price}  {o.Size}");
            }
            Log.Info("-----------------------");
            foreach (Order o in buyOrders)
            {
                Log.Info($"{o.Price}  {o.Size}");
            }
        }
    }
}