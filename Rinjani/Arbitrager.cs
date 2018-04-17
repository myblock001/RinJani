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

        public Arbitrager(IQuoteAggregator quoteAggregator,
            IConfigStore configStore,
            IBalanceService positionService,
            IBrokerAdapterRouter brokerAdapterRouter)
        {
            _quoteAggregator = quoteAggregator ?? throw new ArgumentNullException(nameof(quoteAggregator));
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _brokerAdapterRouter = brokerAdapterRouter ?? throw new ArgumentNullException(nameof(brokerAdapterRouter));
            _positionService = positionService ?? throw new ArgumentNullException(nameof(positionService));
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
            decimal price = Math.Max(bestBidZb.Price, bestBidZb.BasePrice) - 0.01m;
            decimal invertedSpread = price - bestAskHpx.Price;
            decimal availableVolume = Util.RoundDown(Math.Min(bestBidZb.Volume, bestAskHpx.Volume), 3);
            var balanceMap = _positionService.BalanceMap;
            decimal allowedSizeHpx = balanceMap[bestAskHpx.Broker].Cash / bestAskHpx.Price;
            decimal allowedSizeZb = balanceMap[bestBidZb.Broker].Hsr;
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
            decimal price = Math.Min(bestAskZb.Price, bestAskZb.BasePrice) + 0.01m;
            decimal invertedSpread = bestBidHpx.Price - price;

            decimal availableVolume = Util.RoundDown(Math.Min(bestAskZb.Volume, bestBidHpx.Volume), 3);
            
            var balanceMap = _positionService.BalanceMap;
            decimal allowedSizeHpx = balanceMap[bestBidHpx.Broker].Hsr;
            decimal allowedSizeZb = balanceMap[bestAskZb.Broker].Cash / bestAskZb.Price;
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
                BestOrderZb = new Quote(Broker.Zb, QuoteSide.Ask, price, bestBidZb.BasePrice, _activeOrders[0].FilledSize),
            };
            ZbExecuteOrder(result);
            return;
        }

        void ZbBuyOrderDeal()
        {
            var config = _configStore.Config;
            ///Hpx卖价低于Zb基准价
            var bestAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
            .OrderByDescending(q => q.Price).FirstOrDefault();
            if (bestAskZb == null)
            {
                return;
            }
            decimal price = Math.Min(bestAskZb.Price, bestAskZb.BasePrice) + 0.01m;
            SpreadAnalysisResult result = new SpreadAnalysisResult
            {
                BestOrderZb = new Quote(Broker.Zb, QuoteSide.Bid, price, bestAskZb.BasePrice, _activeOrders[0].FilledSize),
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

            Log.Info(string.Format("套利方向 Broker1 {0}", bestOrderHpx.Side==QuoteSide.Ask?"卖单":"买单"));
            Log.Info(string.Format("Broker1价格 {0},  Broker2基准价 {1}", bestOrderHpx.Price, result.BestOrderZb.BasePrice));
            Log.Info(string.Format("套利点{0}%,差异点{1}%", config.ArbitragePoint, (100*invertedSpread/ result.BestOrderZb.BasePrice).ToString("0.00")));

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

        private void ZbExecuteOrder(SpreadAnalysisResult result)
        {
            var config = _configStore.Config;
            var bestOrderZb = result.BestOrderZb;

            Log.Info("{0,-17}: {1}", "Zb Order ", bestOrderZb);

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
                if (bestOrderZb.Side == QuoteSide.Ask)
                    zbHsrBalance = 0;
                else
                    zbCashBalance = 0;
                _activeOrders.Clear();
            }
            while (_activeOrders.Count <= 1|| _activeOrders[_activeOrders.Count-1].BrokerOrderId==null)
            {
                Sleep(config.SleepAfterSend);
                Log.Info("Zb Order failure,Re-order", bestOrderZb);
                ZbExecuteOrder(result);
            }
            Sleep(config.SleepAfterSend);
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
                    if (exeTimes < 2)
                        HpxCheckOrderState();
                    _brokerAdapterRouter.Cancel(order);
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
                Log.Info(Resources.BothLegsAreSuccessfullyFilled);
                Log.Info("Hpx order Fill price is {0}", order.Price);
                return;
            }
        }

        private decimal ZbFilledSize = 0;
        private decimal ZbAverageFilledPrice = 0;
        private void ZbCheckOrderState()
        {
            if (_activeOrders.Count <2)
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
                    ZbFilledSize = 0;
                    for(int j=1;j< _activeOrders.Count;j++)
                    { 
                        ZbFilledSize += _activeOrders[j].FilledSize;
                    }
                    if (ZbFilledSize > order.Size - config.MinSize)
                        order.Status = OrderStatus.Filled;
                    else
                    {
                        _quoteAggregator.Aggregate();//更新ticker数据
                        if (order.Side == OrderSide.Buy)
                        {
                            var bestAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
                                .OrderByDescending(q => q.Price).FirstOrDefault();
                            if (bestAskZb == null)
                            {
                                throw new InvalidOperationException(Resources.NoBestAskWasFound);
                            }
                            decimal price = Math.Min(bestAskZb.Price, bestAskZb.BasePrice);
                            if (order.Price < price)
                            {
                                _brokerAdapterRouter.Cancel(order);
                                order.Status = OrderStatus.Filled;
                                order.Size = order.FilledSize;
                                if (order.FilledSize < config.MinSize)
                                    _activeOrders.Remove(order);
                                SpreadAnalysisResult result = new SpreadAnalysisResult
                                {
                                    BestOrderZb = new Quote(Broker.Zb, QuoteSide.Ask, price, bestAskZb.BasePrice, _activeOrders[0].FilledSize - ZbFilledSize),
                                };
                                ZbExecuteOrder(result);
                                continue;
                            }
                        }
                        else
                        {
                            var bestAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
                                .OrderByDescending(q => q.Price).FirstOrDefault();
                            if (bestAskZb == null)
                            {
                                throw new InvalidOperationException(Resources.NoBestAskWasFound);
                            }
                            decimal price = Math.Min(bestAskZb.Price, bestAskZb.BasePrice);
                            if (order.Price > price)
                            {
                                _brokerAdapterRouter.Cancel(order);
                                order.Status = OrderStatus.Filled;
                                order.Size = order.FilledSize;
                                if (order.FilledSize < config.MinSize)
                                    _activeOrders.Remove(order);
                                SpreadAnalysisResult result = new SpreadAnalysisResult
                                {
                                    BestOrderZb = new Quote(Broker.Zb, QuoteSide.Ask, price, bestAskZb.BasePrice, _activeOrders[0].FilledSize),
                                };
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
            var cashMarginType = brokerConfig.CashMarginType;
            var leverageLevel = brokerConfig.LeverageLevel;
            var order = new Order(quote.Broker, orderSide, targetVolume, quote.Price, cashMarginType, orderType,
                leverageLevel);
            _brokerAdapterRouter.Send(order);
            _activeOrders.Add(order);
        }

        private void Arbitrage()
        {
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

                LiquidBot();
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
            catch(Exception ex)
            {
                Log.Debug($"GetOrdersState:{ex.Message}");
                //Log.Info($"GetOrdersState:{ex.Message}");
                ordersState = null;
            }
        }


        public decimal zbCashBalance = 0;
        public decimal zbHsrBalance = 0;
        private readonly List<Order> _pengdingOrdersHpx = new List<Order>();
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
                .Where(q => q.Broker == Broker.Zb).Where(q => q.Price < highestBidPrice)
                .OrderByDescending(q => q.Price);
            if (cpyBidZb.Count() == 0)
                return;
            var cpyAskZb = _quoteAggregator.Quotes.Where(q => q.Side == QuoteSide.Ask)
                .Where(q => q.Broker == Broker.Zb).Where(q => q.Price > lowestAskPrice)
                .OrderBy(q => q.Price);
            if (cpyAskZb.Count() == 0)
                return;
            var allBuyOrderHpx = _pengdingOrdersHpx.Where(q => q.Side == OrderSide.Buy)
                .OrderByDescending(q => q.Price);

            if(allBuyOrderHpx.Count()==0)
            {
                foreach(var bid in cpyBidZb)
                {
                    bid.Broker = Broker.Hpx;
                    Log.Info($"正在复制买单，当前价格{bid.Price},当前数量{bid.Volume * config.VolumeRatio / 100}");
                    //SendOrder(bid, bid.Volume*config.VolumeRatio/100, OrderType.Limit);                  
                    SendOrder(bid, 0.02m, OrderType.Limit);

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
                        _positionService.GetBalances();
                        Sleep(config.SleepAfterSend);
                        break;
                    }
                    _pengdingOrdersHpx.Add(_activeOrders[_activeOrders.Count - 1]);
                    _activeOrders.Clear();
                    Sleep(config.SleepAfterSend);
                }
            }
            else
            {
                bool exe_flag = true;
                GetOrdersState(1, 0, Broker.Hpx);
                if (ordersState == null)
                    return;
                while (exe_flag)
                {
                    foreach (var order in allBuyOrderHpx)
                    {
                        var orderState = ordersState.Where(q => order.BrokerOrderId == q.id).FirstOrDefault();
                        if (orderState == null)
                            continue;
                        decimal lastPendingSize = order.PendingSize;
                        orderState.SetOrder(order);
                        decimal curFilledSize = lastPendingSize - order.PendingSize;
                        if (curFilledSize > config.MinSize)
                        {
                            _positionService.BalanceMap[Broker.Hpx].Cash -= curFilledSize * order.Price;
                            if (zbHsrBalance == 0)
                            {
                                Sleep(config.SleepAfterSend);
                                break;
                            }
                            Order new_order = new Order(order.Broker, order.Side, curFilledSize, order.Price, order.CashMarginType,
                            order.Type, order.LeverageLevel);
                            new_order.Size = curFilledSize;
                            new_order.FilledSize = curFilledSize;
                            new_order.BrokerOrderId = order.BrokerOrderId;
                            _activeOrders.Add(new_order);
                            Log.Info($"Hpx订单部分成交，成交价格{new_order.Price},成交数量{new_order.FilledSize},Zb开始下单");
                            ZbSellOrderDeal();
                            ZbCheckOrderState();
                            GetOrdersState(1, 0, Broker.Hpx);
                            _activeOrders.Clear();
                            exe_flag = true;
                            Sleep(config.SleepAfterSend);
                            break;
                        }
                        if (order.Price > highestBidPrice)
                        {
                            Log.Info($"Hpx买单价格为{order.Price},不符合条件，删除");
                            _brokerAdapterRouter.Cancel(order);
                            _pengdingOrdersHpx.Remove(order);
                            allBuyOrderHpx = _pengdingOrdersHpx.Where(q => q.Side == OrderSide.Buy)
                             .OrderByDescending(q => q.Price);
                            exe_flag = true;
                            Sleep(config.SleepAfterSend);
                            break;
                        }
                        exe_flag = false;
                    }
                }

                Sleep(config.SleepAfterSend);
                var bestBuyOrderHpx = _pengdingOrdersHpx.Where(q => q.Side == OrderSide.Buy)
                    .OrderByDescending(q => q.Price).FirstOrDefault();
                foreach (var bid in cpyBidZb)
                {
                    if (bid.Price <= bestBuyOrderHpx.Price)
                        continue;
                    bid.Broker = Broker.Hpx;
                    SendOrder(bid, bid.Volume*config.VolumeRatio/100, OrderType.Limit);
                    Sleep(config.SleepAfterSend);
                    Log.Info($"正在复制买单，当前价格{bid.Price},当前数量{bid.Volume * config.VolumeRatio / 100}");
                    if (_activeOrders[_activeOrders.Count-1].BrokerOrderId == "0x3fffff")
                    {
                        _activeOrders.Clear();
                        Log.Info("Hpx买单余额不足");
                        Sleep(config.SleepAfterSend);
                        _positionService.GetBalances();
                        Sleep(config.SleepAfterSend);
                        break;
                    }

                    if (_activeOrders[_activeOrders.Count - 1].BrokerOrderId == null)
                    {
                        _activeOrders.RemoveAt(_activeOrders.Count - 1);
                        if (_activeOrders.Count == 0)
                        {
                            Sleep(config.SleepAfterSend);
                            break;
                        }
                    }
                    _pengdingOrdersHpx.Add(_activeOrders[_activeOrders.Count - 1]);
                    _activeOrders.Clear();
                    Sleep(config.SleepAfterSend);
                }
            }

            var allSellOrderHpx = _pengdingOrdersHpx.Where(q => q.Side == OrderSide.Sell)
                .OrderBy(q => q.Price);

            if (allSellOrderHpx.Count() == 0)
            {
                foreach (var ask in cpyAskZb)
                {
                    ask.Broker = Broker.Hpx;
                    Log.Info($"正在复制卖单，当前价格{ask.Price},当前数量{ask.Volume * config.VolumeRatio / 100}");
                    //SendOrder(ask, ask.Volume * config.VolumeRatio / 100, OrderType.Limit);
                    SendOrder(ask, 0.02m, OrderType.Limit);

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
                        _positionService.GetBalances();
                        Sleep(config.SleepAfterSend);
                        break;
                    }
                    _pengdingOrdersHpx.Add(_activeOrders[_activeOrders.Count - 1]);
                    _activeOrders.Clear();
                    Sleep(config.SleepAfterSend);
                }
            }
            else
            {
                bool exe_flag = true;
                GetOrdersState(1, 1, Broker.Hpx);
                if (ordersState == null)
                    return;
                while (exe_flag)
                {
                    foreach (var order in allSellOrderHpx)
                    {
                        var orderState = ordersState.Where(q => order.BrokerOrderId == q.id).FirstOrDefault();
                        if (orderState == null)
                            continue;
                        decimal lastPendingSize = order.PendingSize;
                        orderState.SetOrder(order);
                        decimal curFilledSize = lastPendingSize - order.PendingSize;
                        if (curFilledSize > config.MinSize)
                        {
                            _positionService.BalanceMap[Broker.Hpx].Hsr -= curFilledSize;
                            if (zbCashBalance == 0)
                            {
                                Sleep(config.SleepAfterSend);
                                break;
                            }
                            Order new_order = new Order(order.Broker, order.Side, order.Size, order.Price, order.CashMarginType,
                            order.Type, order.LeverageLevel);
                            new_order.Size = curFilledSize;
                            new_order.FilledSize = curFilledSize;
                            new_order.BrokerOrderId = order.BrokerOrderId;
                            _activeOrders.Add(new_order);
                            Log.Info($"Hpx订单部分成交，成交价格{new_order.Price},成交数量{new_order.FilledSize},Zb开始下单");
                            ZbBuyOrderDeal();
                            ZbCheckOrderState();
                            _activeOrders.Clear();
                            exe_flag = true;
                            GetOrdersState(1, 1, Broker.Hpx);
                            if (ordersState == null)
                                return;
                            Sleep(config.SleepAfterSend);
                            break;
                        }
                        if (order.Price < lowestAskPrice)
                        {
                            Log.Info($"Hpx卖单价格为{order.Price},不符合条件，删除");
                            _brokerAdapterRouter.Cancel(order);
                            _pengdingOrdersHpx.Remove(order);
                            allSellOrderHpx = _pengdingOrdersHpx.Where(q => q.Side == OrderSide.Sell)
                             .OrderBy(q => q.Price);
                            exe_flag = true;
                            Sleep(config.SleepAfterSend);
                            break;
                        }
                        exe_flag = false;
                    }
                }

                Sleep(config.SleepAfterSend);
                var bestShellOrderHpx = _pengdingOrdersHpx.Where(q => q.Side == OrderSide.Buy)
                     .OrderByDescending(q => q.Price).FirstOrDefault();
                foreach (var ask in cpyAskZb)
                {
                    if (ask.Price >= bestShellOrderHpx.Price)
                        continue;
                    ask.Broker = Broker.Hpx;
                    SendOrder(ask, ask.Volume * config.VolumeRatio / 100, OrderType.Limit);
                    Sleep(config.SleepAfterSend);
                    Log.Info($"正在复制卖单，当前价格{ask.Price},当前数量{ask.Volume * config.VolumeRatio / 100}");
                    if (_activeOrders[_activeOrders.Count - 1].BrokerOrderId == "0x3fffff")
                    {
                        _activeOrders.Clear();
                        Log.Info("Hpx卖单余额不足");
                        Sleep(config.SleepAfterSend);
                        _positionService.GetBalances();
                        Sleep(config.SleepAfterSend);
                        break;
                    }

                    if (_activeOrders[_activeOrders.Count - 1].BrokerOrderId == null)
                    {
                        _activeOrders.RemoveAt(_activeOrders.Count - 1);
                        if (_activeOrders.Count == 0)
                        {
                            Sleep(config.SleepAfterSend);
                            break;
                        }
                    }
                    _pengdingOrdersHpx.Add(_activeOrders[_activeOrders.Count - 1]);
                    _activeOrders.Clear();
                    Sleep(config.SleepAfterSend);
                }
            }
        }    
    }
}