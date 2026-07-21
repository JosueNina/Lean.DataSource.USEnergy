### Introduction

The EIA Electricity dataset reports how the U.S. power grid actually ran, day by day. Every day each balancing authority tells the Energy Information Administration how much electricity its region consumed, how much it had forecast a day earlier, how much it generated, how much it moved across its borders, and which fuels produced that generation. Coverage starts in 2019 and spans 81 balancing authorities, from the large market operators (PJM, ERCOT, CAISO, MISO, NYISO, ISO New England, SPP) down to individual utilities and the regional aggregates.

Electricity is where several macro stories become measurable before they show up in prices. A heat wave lands as a demand spike against a stale forecast. A cold snap shows up as gas burn. The energy transition shows up as coal losing share to wind and solar, day by day, region by region.

### About the Provider

The U.S. Energy Information Administration (EIA) is the statistical agency of the Department of Energy and the official source for U.S. energy statistics. Form EIA-930 collects operating data directly from the balancing authorities that run the grid and publishes it through the Hourly Electric Grid Monitor. The data is a public record and is released free of charge through the EIA API.

QuantConnect processes and caches the EIA-930 data so it is delivered to your algorithm as it was published, with no look-ahead.

### Getting Started

```python
self._pjm = self.add_data(EIAElectricity, EIA.BalancingAuthorities.PJM, Resolution.DAILY).symbol
```
```csharp
_pjm = AddData<EIAElectricity>(EIA.BalancingAuthorities.PJM, Resolution.Daily).Symbol;
```

### Data Summary

| Property | Value |
| --- | --- |
| Start Date | January 2019 |
| Asset Coverage | 81 US Balancing Authorities |
| Data Density | Sparse |
| Resolution | Daily |
| Timezone | New York |
| Data Points | 210,512 |

A single `EIAElectricity` class carries everything one balancing authority reports for one day:

| Group | Fields |
| --- | --- |
| Grid operations | Demand, day-ahead demand forecast, net generation, total interchange |
| Fuel mix | Coal, natural gas, nuclear, hydro, wind, solar, geothermal, oil, other, unknown |
| Storage | Pumped storage, battery, other storage, unknown storage |
| Hybrid renewables | Solar with storage, wind with storage |

Every value is in megawatthours. The three headline metrics balance: net generation minus net interchange equals demand.

Fields are nullable. A blank means the balancing authority does not report that series at all, which is a different statement from a zero. Small authorities report demand but no fuel split, and the storage and hybrid categories only exist in recent years, so a zero is a real reading (no wind generated that day) while a null is an absence.

### Example Applications

The EIA Electricity dataset enables trading strategies driven by grid fundamentals. Examples include:

- Trading utility equities on load surprise, where actual demand runs above the day-ahead forecast.
- Trading natural gas on power-sector gas burn, using gas generation as a share of the fuel mix.
- Trading coal and gas producers on fuel substitution as their share of generation shifts.
- Trading renewable exposure on wind and solar penetration by region.
- Trading regional stress using net interchange, which shows which grids are importing to cover load.

### Data Point Attributes

The EIA Electricity dataset provides `EIAElectricity` objects.

### Revisions

EIA-930 values are preliminary when first published and are revised over the following days as balancing authorities finalize their numbers. QuantConnect ingests them as published, so a backtest sees what a live algorithm would have seen that day. The revision behavior is a property of the source.
