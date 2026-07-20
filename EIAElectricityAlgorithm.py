# QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
# Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

from AlgorithmImports import *


class EIAElectricityAlgorithm(QCAlgorithm):
    '''Example algorithm using EIA-930 grid operations as a source of alpha. A single subscription for
    PJM carries demand, the day-ahead forecast, net generation, interchange and the full fuel mix. The
    algorithm trades on load surprise: days where actual demand runs above what PJM forecast a day
    ahead mean the grid was tighter than the market expected. Grid data is a signal feed (not
    tradeable), so the algorithm trades a separate, tradeable security (SPY).'''

    def initialize(self):
        self.set_start_date(2020, 6, 1)
        self.set_end_date(2020, 9, 1)
        self.set_cash(100000)

        self._spy = self.add_equity("SPY", Resolution.DAILY).symbol

        # One subscription for PJM carries every metric, via the readable helper.
        self._pjm = self.add_data(EIAElectricity, EIA.BalancingAuthorities.PJM, Resolution.DAILY).symbol

    def on_data(self, slice):
        data = slice.get(EIAElectricity)
        if self._pjm not in data:
            return

        grid = data[self._pjm]
        if grid.demand is None or grid.demand_forecast is None or not grid.demand_forecast:
            return

        # Load surprise as a fraction of the forecast, so it is comparable across days.
        load_surprise = (grid.demand - grid.demand_forecast) / grid.demand_forecast

        gas_share = None
        if grid.natural_gas is not None and grid.net_generation:
            gas_share = grid.natural_gas / grid.net_generation

        self.debug(f"{self.time:%Y-%m-%d} PJM - demand: {grid.demand}, forecast: {grid.demand_forecast}, "
                   f"load surprise: {load_surprise:.4f}, gas share: {gas_share}")

        # Demand running more than 2% above forecast is a tight grid: go long.
        # Demand undershooting by the same margin is slack: step aside.
        if load_surprise > 0.02:
            self.set_holdings(self._spy, 1)
        elif load_surprise < -0.02:
            self.liquidate(self._spy)

    def on_order_event(self, order_event):
        if order_event.status == OrderStatus.FILLED:
            self.debug(f"{self.time} - Filled: {order_event.symbol} {order_event.fill_quantity}")
