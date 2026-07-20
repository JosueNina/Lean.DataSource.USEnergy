# Lean.DataSource.USEnergy

Production implementation for the U.S. Energy Information Administration (EIA) datasets. EIA is a
multi-product provider and this repo is its provider-level home, so it ships more than one dataset:

- **US Energy (petroleum)** — the original dataset: EIA petroleum series (stocks, production, imports,
  prices) addressed by series id. Class `USEnergy`, data under `alternative/usenergy/`.
- **EIA Electricity (Form EIA-930)** — daily U.S. electric grid operations by balancing authority.
  Class `EIAElectricity`, data under `alternative/eia/electricity/`.

Both are macro signal feeds, not tradeable instruments: an algorithm reads the series and trades a
separate, tradeable security. Data comes from the public EIA API (free key) and is unlinked.

## EIA Electricity data model

A single `EIAElectricity` class carries everything one balancing authority reports for one day, in
wide format:

- **Grid operations** — actual demand, the day-ahead demand forecast, total net generation, and net
  interchange with neighbouring authorities (positive exports, negative imports).
- **Fuel mix** — net generation split across all sixteen fuel types the form reports: coal, natural
  gas, nuclear, hydro, wind, solar, geothermal, petroleum, other and unknown, plus the storage
  categories (pumped storage, battery, other and unknown storage) and the hybrid renewables (solar and
  wind with integrated battery storage).

The two API routes behind it, `daily-region-data` and `daily-fuel-type-data`, share the same key
(respondent, period) and the same daily cadence, so one subscription gives the whole picture for that
authority. Every value is in megawatthours and parsed as `decimal?`: a blank cell stays null rather
than becoming a fake zero, which matters because zero is a real reading (an authority genuinely
generating no wind on a given day) and distinct from an authority that has no wind fleet at all.

The three headline metrics balance: net generation minus net interchange equals demand.

## Usage

Balancing authorities are addressed via the generated `EIA.BalancingAuthorities` helper of readable
named constants, which resolve to the EIA-930 code the files are named after. The major operators also
carry their common short name (ERCOT, CAISO, NYISO, ISONE, SPP, BPA) alongside the derived one:

```csharp
// C#
var pjm = AddData<EIAElectricity>(EIA.BalancingAuthorities.PJM, Resolution.Daily).Symbol;
```

```python
# Python
self.pjm = self.add_data(EIAElectricity, EIA.BalancingAuthorities.PJM, Resolution.DAILY).symbol
```

## Timing and the day boundary

EIA-930 is delivered daily. `Time` is the operating day and `EndTime` is one day later, the close of
the daily bar, which is when LEAN fires the data point. A completed day is only delivered once it is
over, so a backtest never sees a day before it ended.

The daily routes publish the same day under several timezone conventions (the daily total depends on
where you draw midnight), so the processor asks for the Eastern boundary. That gives the dataset one
uniform day across all balancing authorities and sets `DataTimeZone` to New York, matching our other
U.S. agency datasets (BEA, CFTC, FINRA). Eastern authorities like PJM see their exact local day;
western authorities like CAISO see a day cut on Eastern midnight, running 9pm to 9pm Pacific.

Worth stating plainly: EIA-930 values are preliminary when first published and get revised over the
following days. The processor ingests them as published, which is what keeps backtests point-in-time
honest. The revisions are a property of the source, not a defect in the dataset.

## Processing

`DataProcessing/` holds the C# processor for electricity (`process`). It reads the balancing authority
list from the live respondent facet, so a new authority is picked up without a code change. The whole
daily history is a few thousand rows per authority, under the API's page cap, so one request per route
covers it, with retry and backoff for the transient failures a public API throws. Writes merge
idempotently with the published history, so an incremental run appends a single day.

`DataProcessing/generate_balancing_authorities.py` regenerates `EIA.BalancingAuthorities.cs` from the
same facet.

Petroleum processing still lives in `process.ipynb` and is untouched by the electricity work.

# Implementing your own data source

To learn more about implementing your own data source for our marketplace, visit the
[LeanDataSdk](https://github.com/QuantConnect/LeanDataSdk) repository for more information.
