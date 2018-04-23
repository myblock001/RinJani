using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using static System.Threading.Thread;
using Rinjani.Properties;
using Newtonsoft.Json.Linq;
using Rinjani.Hpx;
using System.Threading;

namespace Rinjani
{
    public class Arbitrager : IArbitrager
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private int work_mode = 0;//0/1=arbitrager/liquid bot
        private readonly List<Order> _activeOrders = new List<Order>();
        private readonly List<Order> _activeOrdersHpx = new List<Order>();
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

        Thread thpx;
        Thread tzb;
        public void Start()
        {
            Log.Info(Resources.StartingArbitrager, nameof(Arbitrager));
            thpx = new Thread(HpxWork);
            thpx.IsBackground = true; ;
            thpx.Start();
            tzb = new Thread(ZbWork);
            tzb.IsBackground = true; ;
            tzb.Start();
            thpx.Join();
            tzb.Join();
            Log.Info(Resources.StartedArbitrager, nameof(Arbitrager));
        }

        void HpxBuyOrderDeal()
        {
            var config = _configStore.Config;
            var bestAskHpx = _quoteAggregator.GetHpxQuote().Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Hpx)
                .OrderBy(q => q.Price).FirstOrDefault();
            if (bestAskHpx == null)
            {
                return;
            }

            ///Hpx卖价低于Zb基准价
            var bestBidZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Zb)
            .OrderByDescending(q => q.Price).FirstOrDefault();
            if (bestBidZb == null)
            {
                return;
            }
            decimal price = Math.Max(bestBidZb.Price, bestBidZb.BasePrice) * _configZb.Leg2ExRate - 0.01m;
            decimal invertedSpread = price - bestAskHpx.Price * _configHpx.Leg2ExRate;
            decimal availableVolume = Util.RoundDown(Math.Min(bestBidZb.Volume, bestAskHpx.Volume), 2);
            decimal allowedSizeHpx = _positionService.BalanceHpx.Leg2 / bestAskHpx.Price;
            decimal allowedSizeZb = _positionService.BalanceZb.Leg1;
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
            var bestBidHpx = _quoteAggregator.GetHpxQuote().Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Hpx)
                .OrderByDescending(q => q.Price).FirstOrDefault();
            if (bestBidHpx == null)
            {
                return;
            }
            ///Hpx买价高于Zb基准价
            var bestAskZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
            .OrderBy(q => q.Price).FirstOrDefault();
            if (bestAskZb == null)
            {
                return;
            }
            decimal price = Math.Min(bestAskZb.Price, bestAskZb.BasePrice) * _configZb.Leg2ExRate + 0.01m;
            decimal invertedSpread = bestBidHpx.Price * _configHpx.Leg2ExRate - price;

            decimal availableVolume = Util.RoundDown(Math.Min(bestAskZb.Volume, bestBidHpx.Volume), 2);
            decimal allowedSizeHpx = _positionService.BalanceHpx.Leg1;
            decimal allowedSizeZb = _positionService.BalanceZb.Leg2 / bestAskZb.Price;
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
            var bestBidZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Zb)
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
            var bestAskZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
            .OrderBy(q => q.Price).FirstOrDefault();
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

            Log.Info(string.Format("套利方向 Broker1 {0}", bestOrderHpx.Side == QuoteSide.Ask ? "卖单" : "买单"));
            Log.Info(string.Format("Broker1价格 {0},  Broker2基准价 {1}", bestOrderHpx.Price, result.BestOrderZb.BasePrice));
            Log.Info(string.Format("套利点{0}%,差异点{1}%", config.ArbitragePoint, (100 * invertedSpread / result.BestOrderZb.BasePrice).ToString("0.00")));

            Log.Info(Resources.SendingOrderTargettingQuote, bestOrderHpx);
            SendOrder(bestOrderHpx, targetVolume, OrderType.Limit);
            if (_activeOrders[0].BrokerOrderId == "0x3fffff")
            {
                Log.Info("Hpx余额不足");
                return;
            }
            if (_activeOrders[0].BrokerOrderId == null)
            {
                Sleep(config.SleepAfterSend);
                return;
            }
            Sleep(config.SleepAfterSend);
            exeTimes = 0;
            HpxCheckOrderState();
        }

        int exeTimes = 0;
        private void HpxCheckOrderState()
        {
            if (_activeOrders.Count != 1)
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
                    if (order.Status != OrderStatus.Canceled)
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
            decimal volume = Util.RoundDown(bestOrderZb.Volume, 2);
            volume = volume == 0 ? 0.01m : volume;
            SendOrder(bestOrderZb, volume, OrderType.Limit);
            if (_activeOrders[_activeOrders.Count - 1].BrokerOrderId == "0x3fffff")
            {
                Log.Info("Zb余额不足");
                _activeOrders.RemoveAt(_activeOrders.Count - 1);
                return;
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
        private int retryCount = 1;
        private void ZbCheckOrderState()
        {
            if (_activeOrders.Count < 2 || work_mode != 0)
                return;

            var config = _configStore.Config;
            _quoteAggregator.ZbAggregate();//更新深度
            Sleep(500);
            var order = _activeOrders[_activeOrders.Count - 1];
            if (retryCount++ >= config.MaxRetryCount)
            {
                Log.Warn(Resources.MaxRetryCountReachedCancellingThePendingOrders);
                if (order.Status != OrderStatus.Filled)
                {
                    _brokerAdapterRouter.Cancel(order);
                }
                retryCount = 1;
                _activeOrders.Clear();
                return;
            }
            Log.Info(Resources.OrderCheckAttempt, retryCount);
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
                if (ZbFilledSize > 0 && ZbFilledSize >= _activeOrders[0].FilledSize - 0.01m)
                {
                    _brokerAdapterRouter.Cancel(order);
                    order.Status = OrderStatus.Filled;
                }
                else
                {
                    if (order.Side == OrderSide.Buy)
                    {
                        var bestAskZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
                            .OrderBy(q => q.Price).FirstOrDefault();
                        if (bestAskZb == null)
                        {
                            return;
                        }
                        decimal price = Math.Min(bestAskZb.Price, bestAskZb.BasePrice);
                        if (retryCount > 4 || price >= _activeOrders[0].Price * (1 - config.StopPoint))
                            price = Math.Max(bestAskZb.Price, bestAskZb.BasePrice);
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
                            if (ZbFilledSize > 0 && ZbFilledSize >= _activeOrders[0].FilledSize - 0.01m)
                            {
                                _brokerAdapterRouter.Cancel(order);
                                Sleep(config.SleepAfterSend);
                                order.Status = OrderStatus.Filled;
                                Log.Info("Zb 订单成交...");
                                return;
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
                            {
                                _activeOrders.Clear();
                                retryCount = 1;
                                return;
                            }
                            ZbExecuteOrder(result);
                            return;
                        }
                    }
                    else
                    {
                        var bestBidZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Zb)
                            .OrderByDescending(q => q.Price).FirstOrDefault();
                        if (bestBidZb == null)
                        {
                            return;
                        }
                        decimal price = Math.Max(bestBidZb.Price, bestBidZb.BasePrice);
                        if (retryCount > 4 || price <= _activeOrders[0].Price * (1 + config.StopPoint))
                            price = Math.Min(bestBidZb.Price, bestBidZb.BasePrice);                       
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
                            if (ZbFilledSize > 0 && ZbFilledSize >= _activeOrders[0].FilledSize - 0.01m)
                            {
                                _brokerAdapterRouter.Cancel(order);
                                Sleep(config.SleepAfterSend);
                                order.Status = OrderStatus.Filled;
                                Log.Info("Zb 订单成交...");
                                return;
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
                            {
                                retryCount = 1;
                                _activeOrders.Clear();
                                return;
                            }
                            ZbExecuteOrder(result);
                            return;
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
                if (ZbFilledSize == 0)
                {
                    _activeOrders.Clear();
                    retryCount = 1;
                    return;
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
                retryCount = 1;
                _activeOrders.Clear();
            }
            Sleep(config.OrderStatusCheckInterval);
        }

        private void SendOrder(Quote quote, decimal targetVolume, OrderType orderType)
        {
            var brokerConfig = _configStore.Config.Brokers.First(x => x.Broker == quote.Broker);
            var orderSide = quote.Side == QuoteSide.Ask ? OrderSide.Sell : OrderSide.Buy;
            var order = new Order(quote.Broker, orderSide, targetVolume, quote.Price, orderType);
            _brokerAdapterRouter.Send(order);
            _activeOrders.Add(order);
        }

        private Order CopyOrder(Quote quote, decimal targetVolume, OrderType orderType)
        {
            var brokerConfig = _configStore.Config.Brokers.First(x => x.Broker == quote.Broker);
            var orderSide = quote.Side == QuoteSide.Ask ? OrderSide.Sell : OrderSide.Buy;
            Order order = new Order(quote.Broker, orderSide, targetVolume, quote.Price, orderType);
            _brokerAdapterRouter.Send(order);
            if (order.BrokerOrderId == null)
            {
                Log.Info("Hpx订单被拒绝");
            }
            else if (order.BrokerOrderId == "0x3fffff")//余额不足
            {
                Log.Info("Hpx余额不足");
            }
            return order;
        }

        public void HpxWork()
        {
            InitHpxOrder();
            while (true)
            {
                try
                {
                    Log.Info(Util.Hr(20) + "ARBITRAGER" + Util.Hr(20));
                    _quoteAggregator.HpxAggregate();//更新ticker数据
                    Arbitrage();
                    Log.Info(Util.Hr(50));
                }
                catch (Exception ex)
                {
                    Log.Error(ex.Message);
                    Log.Debug(ex);
                }
                Sleep(_configStore.Config.IterationInterval);
            }
        }

        public void ZbWork()
        {
            while (true)
            {
                try
                {
                    CheckZbBalance();
                    _quoteAggregator.ZbAggregate();//更新ticker数据
                    if (_activeOrders.Count == 1 && _activeOrders[0].Status == OrderStatus.Filled)
                    {
                        if (_activeOrders[0].Side == OrderSide.Buy)
                            ZbSellOrderDeal();
                        else
                            ZbBuyOrderDeal();
                    }
                    if (_activeOrders.Count > 1)
                    {
                        ZbCheckOrderState();
                        ZbCheckLiquidOrderState();
                    }
                }
                catch(Exception ex)
                {
                    Log.Error(ex.Message);
                    Log.Debug(ex);
                }
                Sleep(_configStore.Config.IterationInterval);
            }
        }

        private void Arbitrage()
        {
            //套利功能
            CheckHpxBalance();
            if (_configStore.Config.Arbitrage && _activeOrders.Count == 0)
            {
                Log.Info(Resources.LookingForOpportunity);                
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
                if (_activeOrders.Count > 0)
                    work_mode = 0;
                else if(_activeOrdersHpx.Count>0)
                {
                    _activeOrders.Add(_activeOrdersHpx[_activeOrdersHpx.Count-1]);
                    _activeOrdersHpx.RemoveAt(_activeOrdersHpx.Count - 1);
                    work_mode = 1;
                }
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
        }

        bool _sendHpxBalanceInfoEmalFlag = true;
        public int hpxloopcnt = 10;
        public void CheckHpxBalance()
        {
            if (hpxloopcnt++ < 5)
            {
                return;
            }
            hpxloopcnt = 0;
            _positionService.GetBalance(Broker.Hpx);
            var config = _configStore.Config;
            if (_positionService.BalanceHpx == null)
                return;
            if (_positionService.BalanceHpx.Leg1 < _configHpx.Leg1ThresholdSendEmail || _positionService.BalanceHpx.Leg2 < _configHpx.Leg2ThresholdSendEmail)
            {
                if (_sendHpxBalanceInfoEmalFlag)
                {
                    string content = $"Hpx Leg1 {_positionService.BalanceHpx.Leg1},Leg2 {_positionService.BalanceHpx.Leg2}";
                    EmailHelper.SendMailUse(_configStore.Config.EmailAddress, "Hpx 余额不足", content);
                    _sendHpxBalanceInfoEmalFlag = false;
                }
            }
            else
            {
                _sendHpxBalanceInfoEmalFlag = true;
            }
        }

        bool _sendZbBalanceInfoEmalFlag = true;
        public int zbloopcnt = 10;
        public void CheckZbBalance()
        {
            if (zbloopcnt++ < 5)
            {
                return;
            }
            zbloopcnt = 0;
            _positionService.GetBalance(Broker.Zb);
            var config = _configStore.Config;
            if (_positionService.BalanceZb == null)
                return;
            if (_positionService.BalanceZb.Leg1 < _configZb.Leg1ThresholdSendEmail || _positionService.BalanceZb.Leg2 < _configZb.Leg2ThresholdSendEmail)
            {
                if (_sendZbBalanceInfoEmalFlag)
                {
                    string content = $"Zb Leg1 {_positionService.BalanceZb.Leg1},Leg2 {_positionService.BalanceZb.Leg2}";
                    EmailHelper.SendMailUse(_configStore.Config.EmailAddress, "Zb 余额不足", content);
                    _sendZbBalanceInfoEmalFlag = false;
                }
            }
            else
            {
                _sendZbBalanceInfoEmalFlag = true;
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
                GetOrdersState(1, 0, Broker.Hpx);
                Log.Debug("InitHpxOrder ordersState is null");
                if (ordersState == null)
                    return;
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

        public void CheckOrdersState()
        {
            var config = _configStore.Config;
            GetOrdersState(1, 0, Broker.Hpx);
            Sleep(config.SleepAfterSend);
            if (ordersState == null)
                return;
            System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start(); //  开始监视代码运行时间
            var buyOrdersState = ordersState.Where(q => q.type == 0);
            foreach (var orderState in buyOrdersState)
            {
                var order = allBuyOrderHpx.Where(q => orderState.id == q.BrokerOrderId).FirstOrDefault();
                if (order == null)
                {
                    order = new Order(Broker.Hpx, OrderSide.Buy, orderState.total_amount, orderState.price, OrderType.Limit);
                    orderState.SetOrder(order);
                    allBuyOrderHpx.Add(order);
                    allBuyOrderHpx.Sort((x, y) => -(x.Price).CompareTo(y.Price));
                }
            }
            foreach (var order in allBuyOrderHpx)
            {
                var orderState = ordersState.Where(q => order.BrokerOrderId == q.id).FirstOrDefault();
                decimal lastPendingSize = order.PendingSize;
                if (orderState == null)
                {
                    _brokerAdapterRouter.Refresh(order);
                    Sleep(config.SleepAfterSend);
                    if (order.Status == OrderStatus.Canceled)
                    {
                        allBuyOrderHpx.Remove(order);
                        PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                        continue;
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
                    new_order.Status = OrderStatus.Filled;
                    _activeOrdersHpx.Add(new_order);
                    Log.Info($"Hpx订单部分成交，成交价格{new_order.Price},成交数量{new_order.FilledSize},Zb开始下单");
                    allBuyOrderHpx.Remove(order);
                    PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                    break;
                }
                if (order.Price > GetHighestBidPrice())
                {
                    Log.Info($"Hpx买单价格为{order.Price},不符合条件，删除");
                    _brokerAdapterRouter.Cancel(order);
                    Sleep(config.SleepAfterSend);
                    allBuyOrderHpx.Remove(order);
                    PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                    break;
                }
            }
            stopwatch.Stop(); //  停止监视
            TimeSpan timespan = stopwatch.Elapsed; //  获取当前实例测量得出的总时间
            double seconds = timespan.TotalSeconds;  //  总秒数
            if(seconds>1)
            {
                GetOrdersState(1, 1, Broker.Hpx);
                Sleep(config.SleepAfterSend);
                if (ordersState == null)
                    return;
            }

            var sellOrdersState = ordersState.Where(q => q.type == 1);
            foreach (var orderState in sellOrdersState)
            {
                var order = allSellOrderHpx.Where(q => orderState.id == q.BrokerOrderId).FirstOrDefault();
                if (order == null)
                {
                    order = new Order(Broker.Hpx, OrderSide.Sell, orderState.total_amount, orderState.price, OrderType.Limit);
                    orderState.SetOrder(order);
                    allSellOrderHpx.Add(order);
                    allSellOrderHpx.Sort((x, y) => (x.Price).CompareTo(y.Price));
                }
            }
            foreach (var order in allSellOrderHpx)
            {
                var orderState = ordersState.Where(q => order.BrokerOrderId == q.id).FirstOrDefault();
                decimal lastPendingSize = order.PendingSize;
                if (orderState == null)
                {
                    _brokerAdapterRouter.Refresh(order);
                    Sleep(config.SleepAfterSend);
                    if (order.Status == OrderStatus.Canceled)
                    {
                        allSellOrderHpx.Remove(order);
                        PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                        continue;
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
                    new_order.Status = OrderStatus.Filled;
                    _activeOrdersHpx.Add(new_order);
                    Log.Info($"Hpx订单部分成交，成交价格{new_order.Price},成交数量{new_order.FilledSize},Zb开始下单");
                    allSellOrderHpx.Remove(order);
                    PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                    break;
                }
                if (order.Price < GetLowestBidPrice())
                {
                    Log.Info($"Hpx卖单价格为{order.Price},不符合条件，删除");
                    _brokerAdapterRouter.Cancel(order);
                    allSellOrderHpx.Remove(order);
                    PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                    break;
                }
            }
        }

        void ZbSellLiquidOrderDeal()
        {
            var config = _configStore.Config;
            ///Hpx卖价低于Zb基准价
            var bestBidZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Zb)
            .OrderByDescending(q => q.Price).FirstOrDefault();
            if (bestBidZb == null)
            {
                return;
            }
            decimal price = Math.Min(bestBidZb.Price, bestBidZb.BasePrice) - 0.01m;
            SpreadAnalysisResult result = new SpreadAnalysisResult
            {
                BestOrderZb = new Quote(Broker.Zb, QuoteSide.Ask, price, bestBidZb.BasePrice, _activeOrders[0].FilledSize),
            };
            ZbExecuteOrder(result);
            return;
        }

        void ZbBuyLiquidOrderDeal()
        {
            var config = _configStore.Config;
            ///Hpx卖价低于Zb基准价
            var bestAskZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
            .OrderBy(q => q.Price).FirstOrDefault();
            if (bestAskZb == null)
            {
                return;
            }
            decimal price = Math.Max(bestAskZb.Price, bestAskZb.BasePrice) + 0.01m;
            SpreadAnalysisResult result = new SpreadAnalysisResult
            {
                BestOrderZb = new Quote(Broker.Zb, QuoteSide.Bid, price, bestAskZb.BasePrice, _activeOrders[0].FilledSize),
            };
            ZbExecuteOrder(result);
            return;
        }

        public int lretryCount = 1;
        private void ZbCheckLiquidOrderState()
        {
            if (_activeOrders.Count < 2 || work_mode != 1)
                return;
            var config = _configStore.Config;
            var order = _activeOrders[_activeOrders.Count - 1];
            Log.Info(Resources.OrderCheckAttempt, lretryCount);
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

            if (lretryCount++ == config.MaxRetryCount)
            {
                Log.Warn(Resources.MaxRetryCountReachedCancellingThePendingOrders);
                if (order.Status != OrderStatus.Filled)
                {
                    _brokerAdapterRouter.Cancel(order);
                }
                lretryCount = 1;
                _activeOrders.Clear();
                return;
            }

            if (order.Status != OrderStatus.Filled)
            {
                Log.Warn("Zb Leg is not filled yet,pending size is {0}", order.PendingSize);
                ZbFilledSize = 0;//第一个order为hpx下单
                for (int j = 1; j < _activeOrders.Count; j++)
                {
                    ZbFilledSize += _activeOrders[j].FilledSize;
                }
                if (ZbFilledSize > 0 && ZbFilledSize >= _activeOrders[0].FilledSize - 0.01m)
                {
                    _brokerAdapterRouter.Cancel(order);
                    Sleep(config.SleepAfterSend);
                    order.Status = OrderStatus.Filled;
                }
                else
                {
                    if (order.Side == OrderSide.Buy)
                    {
                        var bestAskZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Ask).Where(q => q.Broker == Broker.Zb)
                            .OrderBy(q => q.Price).FirstOrDefault();
                        if (bestAskZb == null)
                        {
                            return;
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
                            if (ZbFilledSize > 0 && ZbFilledSize >= _activeOrders[0].FilledSize - 0.01m)
                            {
                                _brokerAdapterRouter.Cancel(order);
                                Sleep(config.SleepAfterSend);
                                order.Status = OrderStatus.Filled;
                                return;
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
                            {
                                _activeOrders.Clear();
                                retryCount = 1;
                                return;
                            }
                            ZbExecuteOrder(result);
                            return;
                        }
                    }
                    else
                    {
                        var bestBidZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Bid).Where(q => q.Broker == Broker.Zb)
                            .OrderByDescending(q => q.Price).FirstOrDefault();
                        if (bestBidZb == null)
                        {
                            return;
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
                            if (ZbFilledSize > 0 && ZbFilledSize >= _activeOrders[0].FilledSize - 0.01m)
                            {
                                _brokerAdapterRouter.Cancel(order);
                                Sleep(config.SleepAfterSend);
                                order.Status = OrderStatus.Filled;
                                return;
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
                            {
                                _activeOrders.Clear();
                                retryCount = 1;
                                return;
                            }
                            ZbExecuteOrder(result);
                            return;
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
                if (ZbFilledSize == 0)
                {
                    lretryCount = 1;
                    _activeOrders.Clear();
                    return;
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
                lretryCount = 1;
                _activeOrders.Clear();
                return;
            }
            Sleep(config.OrderStatusCheckInterval);
        }

        public decimal GetHighestBidPrice()
        {
            var config = _configStore.Config;
            var bestBidZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Bid)
                .Where(q => q.Broker == Broker.Zb).OrderByDescending(q => q.Price).FirstOrDefault();
            if (bestBidZb == null)
                return -1;
            decimal highestBidPrice = bestBidZb.Price * (100 - config.RemovalRatio) / 100;
            return highestBidPrice;
        }

        public decimal GetLowestBidPrice()
        {
            var config = _configStore.Config;
            var bestAskZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Ask)
                .Where(q => q.Broker == Broker.Zb).OrderBy(q => q.Price).FirstOrDefault();
            if (bestAskZb == null)
                return -1;
            decimal lowestAskPrice = bestAskZb.Price * (100 + config.RemovalRatio) / 100;
            return lowestAskPrice;
        }

        private bool startflag = true;
        private List<Order> allBuyOrderHpx = new List<Order>();
        private List<Order> allSellOrderHpx = new List<Order>();
        private void LiquidBot()
        {
            if (ordersState != null)
                ordersState.Clear();
            var config = _configStore.Config;

            decimal highestBidPrice = GetHighestBidPrice();
            var cpyBidZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Bid)
                .Where(q => q.Broker == Broker.Zb).Where(q => q.Price <= highestBidPrice)
                .OrderByDescending(q => q.Price);
            if (cpyBidZb == null || cpyBidZb.Count() == 0|| highestBidPrice == -1)
            {
                Log.Info("Zb未获取到买单数据");
                return;
            }
            highestBidPrice = cpyBidZb.FirstOrDefault().Price;
            decimal lowestAskPrice = GetLowestBidPrice();
            var cpyAskZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Ask)
                .Where(q => q.Broker == Broker.Zb).Where(q => q.Price >= lowestAskPrice)
                .OrderBy(q => q.Price);
            if (cpyAskZb == null || cpyAskZb.Count() == 0 || highestBidPrice == -1)
            {
                Log.Info("Zb未获取到卖单数据");
                return;
            }
            lowestAskPrice = cpyAskZb.FirstOrDefault().Price;

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
                    Order order=CopyOrder(bid, cpyVol, OrderType.Limit);   
                    allBuyOrderHpx.Add(order);
                    allBuyOrderHpx.Sort((x, y) => -(x.Price).CompareTo(y.Price));
                    Sleep(config.SleepAfterSend);
                }
                PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
            }
            else
            {
                Sleep(config.SleepAfterSend);
                CheckOrdersState();
                Sleep(config.SleepAfterSend);
                if (allBuyOrderHpx.Count == 0)
                    return;
                var bestBuyOrderHpx = allBuyOrderHpx[0];
                var worstBuyOrderHpx = allBuyOrderHpx[allBuyOrderHpx.Count - 1];
                foreach (var bid in cpyBidZb)
                {
                    if (bid.Price <= worstBuyOrderHpx.Price && allBuyOrderHpx.Count >= config.CopyQuantity)
                        continue;

                    var tmp_o = allBuyOrderHpx.Where(q => q.Price == bid.Price).FirstOrDefault();
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
                    Order order = CopyOrder(bid, cpyVol, OrderType.Limit);
                    Sleep(config.SleepAfterSend);
                    allBuyOrderHpx.Add(order);
                    allBuyOrderHpx.Sort((x, y) => -(x.Price).CompareTo(y.Price));
                    PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                    CheckOrdersState();
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
                        CheckOrdersState();
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
                    Order order = CopyOrder(ask, cpyVol, OrderType.Limit);                   
                    allSellOrderHpx.Add(order);
                    allSellOrderHpx.Sort((x, y) => (x.Price).CompareTo(y.Price));
                    Sleep(config.SleepAfterSend);
                }
                PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
            }
            else
            {
                CheckOrdersState();
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
                    Order order = CopyOrder(ask, cpyVol, OrderType.Limit);
                    Sleep(config.SleepAfterSend);                  
                    allSellOrderHpx.Add(order);
                    allSellOrderHpx.Sort((x, y) => (x.Price).CompareTo(y.Price));
                    PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                    CheckOrdersState();
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
                        CheckOrdersState();
                        Sleep(config.SleepAfterSend);
                    }
                    PrintOrderInfo(allBuyOrderHpx, allSellOrderHpx);
                }
            }
        }

        void PrintOrderInfo(List<Order> buyOrders, List<Order> sellOrders)
        {
            var cpyBidZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Bid)
                 .Where(q => q.Broker == Broker.Zb).OrderByDescending(q => q.Price).Take(_configStore.Config.CopyQuantity);
            var cpyAskZb = _quoteAggregator.GetZbQuote().Where(q => q.Side == QuoteSide.Ask)
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
            if (_quoteAggregator.GetZbQuote().Count > 0)
                basePrice = _quoteAggregator.GetZbQuote()[0].BasePrice;
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