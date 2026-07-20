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

using System;
using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.DataSource;

namespace QuantConnect.DataLibrary.Tests
{
    /// <summary>
    /// Unit tests for the EIAElectricity data class. These parse in-line sample lines — no external
    /// data files required (CI-safe).
    /// </summary>
    [TestFixture]
    public class EIAElectricityTests
    {
        // A real PJM day straight off the API (2024-01-15, Eastern boundary). PJM reports demand,
        // forecast, net generation, interchange and eight fuels; the geothermal, unknown and storage
        // cells are empty because PJM does not report them.
        private const string SampleLine =
            "20240115,2740736,2737954,2940472,199817,813443,1139488,809590,58037,43142,11414,,20145,40212,,,,,,,";

        private static SubscriptionDataConfig Config(string ticker)
        {
            return new SubscriptionDataConfig(
                typeof(EIAElectricity),
                Symbol.Create(ticker, SecurityType.Base, Market.USA),
                Resolution.Daily,
                TimeZones.NewYork,
                TimeZones.NewYork,
                false, false, false);
        }

        [Test]
        public void ReaderParsesAllColumns()
        {
            var config = Config("PJM");
            var point = new EIAElectricity().Reader(config, SampleLine, DateTime.UtcNow, false) as EIAElectricity;

            Assert.IsNotNull(point);
            Assert.AreEqual(config.Symbol, point.Symbol);
            Assert.AreEqual(new DateTime(2024, 1, 15), point.Time);

            // Region metrics. Value tracks net generation.
            Assert.AreEqual(2740736m, point.Demand);
            Assert.AreEqual(2737954m, point.DemandForecast);
            Assert.AreEqual(2940472m, point.NetGeneration);
            Assert.AreEqual(2940472m, point.Value);
            Assert.AreEqual(199817m, point.TotalInterchange);

            // Fuel mix.
            Assert.AreEqual(813443m, point.Coal);
            Assert.AreEqual(1139488m, point.NaturalGas);
            Assert.AreEqual(809590m, point.Nuclear);
            Assert.AreEqual(58037m, point.Hydro);
            Assert.AreEqual(43142m, point.Wind);
            Assert.AreEqual(11414m, point.Solar);
            Assert.AreEqual(20145m, point.Oil);
            Assert.AreEqual(40212m, point.Other);
        }

        [Test]
        public void NetGenerationLessInterchangeApproximatesDemand()
        {
            // Sanity check on the column mapping itself: EIA-930 balances as
            // net generation - net interchange = demand. If two columns were transposed this breaks.
            var point = new EIAElectricity().Reader(Config("PJM"), SampleLine, DateTime.UtcNow, false) as EIAElectricity;

            var implied = point.NetGeneration.Value - point.TotalInterchange.Value;
            // Daily totals are millions of MWh, so allow a small absolute rounding gap.
            Assert.LessOrEqual(Math.Abs(point.Demand.Value - implied), 500m);
        }

        [Test]
        public void EndTimeIsOneDayAfterTime()
        {
            // The completed day is published once it is over, so the bar close is also the
            // point-in-time correct delivery moment. LEAN fires the point at EndTime.
            var point = new EIAElectricity().Reader(Config("PJM"), SampleLine, DateTime.UtcNow, false) as EIAElectricity;

            Assert.AreEqual(new DateTime(2024, 1, 16), point.EndTime);
            Assert.AreEqual(TimeSpan.FromDays(1), point.EndTime - point.Time);
        }

        [Test]
        public void SettingEndTimeShiftsTime()
        {
            // EndTime is derived, so its setter has to walk Time back by the period rather than
            // clobbering it.
            var point = new EIAElectricity { EndTime = new DateTime(2024, 1, 16) };

            Assert.AreEqual(new DateTime(2024, 1, 15), point.Time);
        }

        [Test]
        public void EmptyCellsBecomeNull()
        {
            // A blank cell must parse to null, not zero. Zero is a real value here: a balancing
            // authority genuinely generating no wind at night reports 0, which is not the same as
            // one that has no wind fleet at all and reports nothing.
            var point = new EIAElectricity().Reader(Config("PJM"), SampleLine, DateTime.UtcNow, false) as EIAElectricity;

            Assert.IsNull(point.Geothermal);
            Assert.IsNull(point.Unknown);
            Assert.IsNull(point.PumpedStorage);
            Assert.IsNull(point.Battery);
            Assert.IsNull(point.OtherStorage);
            Assert.IsNull(point.UnknownStorage);
            Assert.IsNull(point.SolarWithStorage);
            Assert.IsNull(point.WindWithStorage);
        }

        [Test]
        public void NegativeAndZeroValuesParse()
        {
            // Interchange goes negative when importing and storage goes negative while charging.
            var line = "20240115,113297,113196,121696,-8403,0,43401,33784,4144,0,29,,1272,1434,,-150,-42,,,,";
            var point = new EIAElectricity().Reader(Config("CISO"), line, DateTime.UtcNow, false) as EIAElectricity;

            Assert.AreEqual(-8403m, point.TotalInterchange);
            Assert.AreEqual(0m, point.Coal);
            Assert.AreEqual(0m, point.Wind);
            Assert.AreEqual(-150m, point.PumpedStorage);
            Assert.AreEqual(-42m, point.Battery);
        }

        [Test]
        public void CloneCopiesAllProperties()
        {
            var original = new EIAElectricity().Reader(Config("PJM"), SampleLine, DateTime.UtcNow, false) as EIAElectricity;
            var clone = original.Clone() as EIAElectricity;

            Assert.IsNotNull(clone);
            Assert.AreEqual(original.Symbol, clone.Symbol);
            Assert.AreEqual(original.Time, clone.Time);
            Assert.AreEqual(original.EndTime, clone.EndTime);
            Assert.AreEqual(original.Value, clone.Value);
            Assert.AreEqual(original.Demand, clone.Demand);
            Assert.AreEqual(original.DemandForecast, clone.DemandForecast);
            Assert.AreEqual(original.NetGeneration, clone.NetGeneration);
            Assert.AreEqual(original.TotalInterchange, clone.TotalInterchange);
            Assert.AreEqual(original.Coal, clone.Coal);
            Assert.AreEqual(original.NaturalGas, clone.NaturalGas);
            Assert.AreEqual(original.Nuclear, clone.Nuclear);
            Assert.AreEqual(original.Hydro, clone.Hydro);
            Assert.AreEqual(original.Wind, clone.Wind);
            Assert.AreEqual(original.Solar, clone.Solar);
            Assert.AreEqual(original.Geothermal, clone.Geothermal);
            Assert.AreEqual(original.Oil, clone.Oil);
            Assert.AreEqual(original.Other, clone.Other);
            Assert.AreEqual(original.Unknown, clone.Unknown);
            Assert.AreEqual(original.PumpedStorage, clone.PumpedStorage);
            Assert.AreEqual(original.Battery, clone.Battery);
            Assert.AreEqual(original.OtherStorage, clone.OtherStorage);
            Assert.AreEqual(original.UnknownStorage, clone.UnknownStorage);
            Assert.AreEqual(original.SolarWithStorage, clone.SolarWithStorage);
            Assert.AreEqual(original.WindWithStorage, clone.WindWithStorage);
        }

        [Test]
        public void ColumnLayoutMatchesParser()
        {
            // The processor writes its columns off these two lists, the Reader indexes them by hand.
            // If someone adds a fuel type to the list without touching the parser, this catches it.
            Assert.AreEqual(4, EIAElectricity.RegionColumns.Count);
            Assert.AreEqual(16, EIAElectricity.FuelColumns.Count);
            Assert.AreEqual(21, SampleLine.Split(',').Length);
        }

        [Test]
        public void GetSourceReturnsLocalFile()
        {
            var source = new EIAElectricity().GetSource(Config("PJM"), DateTime.UtcNow, false);

            Assert.AreEqual(SubscriptionTransportMedium.LocalFile, source.TransportMedium);
            Assert.IsTrue(source.Source.Contains("eia"), $"Source '{source.Source}' missing vendor folder");
            Assert.IsTrue(source.Source.Contains("electricity"), $"Source '{source.Source}' missing dataset folder");
            Assert.IsTrue(source.Source.Contains("pjm.csv"), $"Source '{source.Source}' missing file name");
        }

        [Test]
        public void GetSourceReturnsSamePathInLiveMode()
        {
            // Live trading resolves the same local file as backtesting, so a daily live run reads the
            // file the incremental processor appended to.
            var backtest = new EIAElectricity().GetSource(Config("PJM"), DateTime.UtcNow, false);
            var live = new EIAElectricity().GetSource(Config("PJM"), DateTime.UtcNow, true);

            Assert.AreEqual(SubscriptionTransportMedium.LocalFile, live.TransportMedium);
            Assert.AreEqual(backtest.Source, live.Source);
        }

        [Test]
        public void ReaderFiresOnAForecastOnlyRow()
        {
            // The most recent day is published with only the day-ahead forecast, the rest still empty.
            // The Reader must still produce a point (so a live algorithm sees the forecast) rather than
            // dropping the row.
            var line = "20260720,,2544185,,,,,,,,,,,,,,,,,,";
            var point = new EIAElectricity().Reader(Config("PJM"), line, DateTime.UtcNow, false) as EIAElectricity;

            Assert.IsNotNull(point);
            Assert.AreEqual(new DateTime(2026, 7, 20), point.Time);
            Assert.AreEqual(2544185m, point.DemandForecast);
            Assert.IsNull(point.Demand);
            Assert.IsNull(point.NetGeneration);
        }

        [Test]
        public void FlagsAreCorrect()
        {
            var instance = new EIAElectricity();

            Assert.AreEqual(Resolution.Daily, instance.DefaultResolution());
            Assert.Contains(Resolution.Daily, instance.SupportedResolutions());
            Assert.IsFalse(instance.RequiresMapping());
            Assert.IsTrue(instance.IsSparseData());
            Assert.AreEqual(TimeZones.NewYork, instance.DataTimeZone());
        }

        [Test]
        public void BalancingAuthorityConstantsMapToCodes()
        {
            // The short aliases are what algorithms actually type; the codes are what the files are
            // named after.
            Assert.AreEqual("PJM", EIA.BalancingAuthorities.PJM);
            Assert.AreEqual("ERCO", EIA.BalancingAuthorities.ERCOT);
            Assert.AreEqual("CISO", EIA.BalancingAuthorities.CAISO);
            Assert.AreEqual("MISO", EIA.BalancingAuthorities.MISO);
            Assert.AreEqual("NYIS", EIA.BalancingAuthorities.NYISO);
            Assert.AreEqual("ISNE", EIA.BalancingAuthorities.ISONE);
            Assert.AreEqual("SWPP", EIA.BalancingAuthorities.SPP);
            Assert.AreEqual("BPAT", EIA.BalancingAuthorities.BPA);
        }
    }
}
