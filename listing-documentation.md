### Requesting Data

To add EIA Electricity data to your algorithm, call the **AddData** method. The dataset is unlinked, so instead of a security Symbol you pass a balancing authority code. Use the **EIA.BalancingAuthorities** helper to get a readable name for the authority you want. The major operators carry their common short name (PJM, ERCOT, CAISO, MISO, NYISO, ISONE, SPP, BPA). Save a reference to the dataset **Symbol** so you can access the data later in your algorithm.

A single **EIAElectricity** class carries everything one balancing authority reports for one hour:

- **Grid operations**: actual demand, the day-ahead demand forecast, total net generation, and net interchange with neighbouring authorities.
- **Fuel mix**: net generation split across coal, natural gas, nuclear, hydro, wind, solar, geothermal, oil, other and unknown.
- **Storage and hybrids**: pumped storage, battery, other and unknown storage, plus solar and wind paired with integrated battery storage.

Grid data is a signal, not a tradeable instrument, so add a separate tradeable security if you want to place orders.

```python
class EIAElectricityDataAlgorithm(QCAlgorithm):

    def initialize(self) -> None:
        self.set_start_date(2024, 1, 1)
        self.set_end_date(2024, 3, 1)
        self.set_cash(100000)

        self._xlu = self.add_equity("XLU", Resolution.HOUR).symbol

        self._pjm = self.add_data(EIAElectricity, EIA.BalancingAuthorities.PJM, Resolution.HOUR).symbol
```
```csharp
public class EIAElectricityDataAlgorithm : QCAlgorithm
{
    private Symbol _xlu, _pjm;

    public override void Initialize()
    {
        SetStartDate(2024, 1, 1);
        SetEndDate(2024, 3, 1);
        SetCash(100000);

        _xlu = AddEquity("XLU", Resolution.Hour).Symbol;

        _pjm = AddData<EIAElectricity>(EIA.BalancingAuthorities.PJM, Resolution.Hour).Symbol;
    }
}
```

### Accessing Data

To get the current EIA Electricity data, index the current [Slice](https://www.quantconnect.com/docs/v2/writing-algorithms/key-concepts/time-modeling/timeslices) with the dataset **Symbol**. **Slice** objects deliver unique events to your algorithm as they happen, but the **Slice** may not contain data for your dataset at every time step. Check that the **Slice** contains the data you want before you index it.

Fields are nullable because a balancing authority only reports the series that apply to it. Small authorities report demand but no fuel split, and the storage and hybrid categories only exist in recent years. A null means the series is absent, which is a different statement from a zero, and a zero is a real reading. Check for a missing value before you act on it.

```python
def on_data(self, slice: Slice) -> None:
    data = slice.get(EIAElectricity)
    if data and self._pjm in data:
        point = data[self._pjm]
        if point.demand is not None and point.demand_forecast is not None:
            self.plot("PJM", "LoadSurprise", point.demand - point.demand_forecast)
```
```csharp
public override void OnData(Slice slice)
{
    var data = slice.Get<EIAElectricity>();
    if (data.ContainsKey(_pjm))
    {
        var point = data[_pjm];
        if (point.Demand.HasValue && point.DemandForecast.HasValue)
        {
            Plot("PJM", "LoadSurprise", point.Demand.Value - point.DemandForecast.Value);
        }
    }
}
```

To iterate through all of the subscribed balancing authorities in the current **Slice**, call the **Get** method.

```python
def on_data(self, slice: Slice) -> None:
    for dataset_symbol, data_point in slice.get(EIAElectricity).items():
        self.log(f"{dataset_symbol}: demand {data_point.demand}, net generation {data_point.net_generation}")
```
```csharp
public override void OnData(Slice slice)
{
    foreach (var kvp in slice.Get<EIAElectricity>())
    {
        Log($"{kvp.Key}: demand {kvp.Value.Demand}, net generation {kvp.Value.NetGeneration}");
    }
}
```

The **Value** property is the headline total net generation, in megawatthours.

### Timing

**Time** is the operating hour in UTC and **EndTime** is one hour later, which is when the data point reaches your algorithm. That matches the EIA-930 publication lag, so an hour is delivered once it has actually been published.

EIA-930 values are preliminary when first published and are revised over the following days. The dataset stores them as published, so a backtest sees what a live algorithm would have seen.

### Historical Data

To get historical EIA Electricity data, call the **History** method with the dataset **Symbol**. If there is no data in the period you request, the history result is empty. The data is hourly, so a request for 168 data points covers roughly one week.

```python
# DataFrame
history_df = self.history(self._pjm, 168, Resolution.HOUR)

# Dataset objects
history_bars = self.history[EIAElectricity](self._pjm, 168, Resolution.HOUR)
```
```csharp
var history = History<EIAElectricity>(_pjm, 168, Resolution.Hour);
```

For more information about historical data, see [History Requests](https://www.quantconnect.com/docs/v2/writing-algorithms/historical-data/history-requests).

### Remove Subscriptions

To remove a subscription to the EIA Electricity dataset, call the **RemoveSecurity** method.

```python
self.remove_security(self._pjm)
```
```csharp
RemoveSecurity(_pjm);
```
