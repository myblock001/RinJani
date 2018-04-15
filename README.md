## How it works
1. Every 3 seconds, the Arbitrager fetches quotes from all configured brokers.
1. Filters out quotes that are not usable for arbitrage. For example, if _MaxShortPosition_ config is 0 and the current position is 0 for a broker, ask quotes for the broker are filtered out.
1. Calculates best ask and best bid. If the spread is not inverted, there is no arbitrage opportunity, so the arbitrager waits for the next iteration.
1. Verifies if there is enough expected profit. If the expected profit is smaller than _MinTargetProfit_ config, the Arbitrager waits for the next iteration.
1. Arbitrage the spread by sending a buy leg and a sell leg to each broker.
1. With 3 seconds interval, the Arbitrager checks if the legs are filled or not.
1. If the both legs are filled, shows the profit. 

## Configuration

All configurations are stored in _config.json_.

### Global Config
|Name|Values|Description|
|----|------|-----------|
|DemoMode|true or false|If it's True, the arbitrager analyzes spreads but doesn't send any trade.|
|PriceMergeSize|integer|Merges small quotes into the specified price ladder before analyzing arbitrage opportunity.|
|MaxSize|decimal|Maximum BTC size to be sent to a broker.|
|MinSize|decimal|Minimum BTC size to be sent to a broker.|
|MinTargetProfit|Integer|Minimum size to try to arbitrage.|
|IterationInterval|Millisecond|Time lapse in milliseconds of an iteration. When it's set to 3000, the quotes fetch and the spreads analysis for all the brokers are done every 3 seconds|
|PositionRefreshInterval|Millisecond|Time lapse in milliseconds of position data refresh. Position data is used to check max exposure and long/short availability for each broker.|
|SleepAfterSend|Millisecond|Time lapse in milliseconds after one arbitrage is done.|
|MaxNetExposure|decimal|Maximum total net exposure. If net exposure qty is larger than this value, Arbitrager stops.| 
|MaxRetryCount|integer|Maximum retry count to check if arbitrage orders are filled or not. If the orders are not filled after the retries, Arbitrager tries to cancel the orders and continues.|
|OrderStatusCheckInterval|Millisecond|Time lapse in milliseconds to check if arbitrage orders are filled or not.|

### Broker config
|Name|Values|Description|
|----|------|-----------|
|Broker|Bitflyer, Quoine or Coincheck|Broker enum|
|Enabled|true or false|Enable the broker for arbitrage|
|Key|string|Broker API Key or Token|
|Secret|string|BrokerAPI Secret|
|MaxLongPosition|decimal|Maximum long position allowed for the broker.|
|MaxShortPosition|decimal|Maximum short position allowed for the broker|
|CashMarginType|Cash, MarginOpen, MarginClose, NetOut|Arbitrage order type. Currently, this option is not fully supported. Please do not change from the default values.|

### Limitations

### Log files
All log files are saved under _logs_ directory. To configure logging, edit _nlog.conf_.

## Running the tests

Run unit tests under Rinjani.Tests project. The unit tests are safely executed with mocks, not against real exchange API.

To run unsafe tests against real exchange API, comment out Ignore attributes in BrokerApiTest.cs file.  

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details

## Inspirations
[Blackbird](https://github.com/butor/blackbird), which targets US exchanges. 
