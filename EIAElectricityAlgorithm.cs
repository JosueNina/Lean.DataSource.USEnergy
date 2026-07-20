/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using QuantConnect.Data;
using QuantConnect.Orders;
using QuantConnect.Algorithm;
using QuantConnect.DataSource;

namespace QuantConnect.DataLibrary.Tests
{
    /// <summary>
    /// Example algorithm using EIA-930 grid operations as a source of alpha. A single subscription for
    /// PJM carries demand, the day-ahead forecast, net generation, interchange and the full fuel mix.
    /// The algorithm trades on load surprise: hours where actual demand runs above what PJM forecast a
    /// day ahead mean the grid is tighter than the market expected. Grid data is a signal feed (not
    /// tradeable), so the algorithm trades a separate, tradeable security (XLU, the utilities ETF).
    /// </summary>
    public class EIAElectricityAlgorithm : QCAlgorithm
    {
        private Symbol _pjm;
        private Symbol _xlu;

        /// <summary>
        /// Initialise the data and resolution required, as well as the cash and start-end dates.
        /// </summary>
        public override void Initialize()
        {
            SetStartDate(2024, 1, 1);
            SetEndDate(2024, 3, 1);
            SetCash(100000);

            _xlu = AddEquity("XLU", Resolution.Hour).Symbol;

            // One subscription for PJM carries every metric, via the readable helper.
            _pjm = AddData<EIAElectricity>(EIA.BalancingAuthorities.PJM, Resolution.Hour).Symbol;
        }

        /// <summary>
        /// OnData event is the primary entry point for your algorithm. Each new data point is here.
        /// </summary>
        /// <param name="slice">Slice object keyed by symbol containing the data</param>
        public override void OnData(Slice slice)
        {
            var data = slice.Get<EIAElectricity>();
            if (!data.ContainsKey(_pjm))
            {
                return;
            }

            var grid = data[_pjm];
            if (!grid.Demand.HasValue || !grid.DemandForecast.HasValue || grid.DemandForecast.Value == 0m)
            {
                return;
            }

            // Load surprise as a fraction of the forecast, so it is comparable across hours.
            var loadSurprise = (grid.Demand.Value - grid.DemandForecast.Value) / grid.DemandForecast.Value;

            decimal? gasShare = null;
            if (grid.NaturalGas.HasValue && grid.NetGeneration.HasValue && grid.NetGeneration.Value != 0m)
            {
                gasShare = grid.NaturalGas.Value / grid.NetGeneration.Value;
            }

            Debug($"{Time:yyyy-MM-dd HH:mm} PJM - demand: {grid.Demand}, forecast: {grid.DemandForecast}, " +
                  $"load surprise: {loadSurprise:F4}, gas share: {gasShare}");

            // Demand running more than 2% above forecast is a tight grid: go long utilities.
            // Demand undershooting by the same margin is slack: step aside.
            if (loadSurprise > 0.02m)
            {
                SetHoldings(_xlu, 1);
            }
            else if (loadSurprise < -0.02m)
            {
                Liquidate(_xlu);
            }
        }

        /// <summary>
        /// Order fill event handler. On an order fill update the resulting information is passed to this method.
        /// </summary>
        /// <param name="orderEvent">Order event details containing details of the events</param>
        public override void OnOrderEvent(OrderEvent orderEvent)
        {
            if (orderEvent.Status == OrderStatus.Filled)
            {
                Debug($"{Time} - Filled: {orderEvent.Symbol} {orderEvent.FillQuantity}");
            }
        }
    }
}
