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
        // A real PJM hour straight off the API (2024-01-15 00:00 UTC). PJM reports demand, forecast,
        // net generation, interchange and eight fuels; the geothermal, unknown and storage cells are
        // empty because PJM does not report them.
        private const string SampleLine =
            "20240115 00:00,113297,113196,121696,8403,33245,43401,33784,4144,3632,29,,1272,1434,,,,,,,";

        private static SubscriptionDataConfig Config(string ticker)
        {
            return new SubscriptionDataConfig(
                typeof(EIAElectricity),
                Symbol.Create(ticker, SecurityType.Base, Market.USA),
                Resolution.Hour,
                TimeZones.Utc,
                TimeZones.Utc,
                false, false, false);
        }

        [Test]
        public void ReaderParsesAllColumns()
        {
            var config = Config("PJM");
            var point = new EIAElectricity().Reader(config, SampleLine, DateTime.UtcNow, false) as EIAElectricity;

            Assert.IsNotNull(point);
            Assert.AreEqual(config.Symbol, point.Symbol);
            Assert.AreEqual(new DateTime(2024, 1, 15, 0, 0, 0), point.Time);

            // Region metrics. Value tracks net generation.
            Assert.AreEqual(113297m, point.Demand);
            Assert.AreEqual(113196m, point.DemandForecast);
            Assert.AreEqual(121696m, point.NetGeneration);
            Assert.AreEqual(121696m, point.Value);
            Assert.AreEqual(8403m, point.TotalInterchange);

            // Fuel mix.
            Assert.AreEqual(33245m, point.Coal);
            Assert.AreEqual(43401m, point.NaturalGas);
            Assert.AreEqual(33784m, point.Nuclear);
            Assert.AreEqual(4144m, point.Hydro);
            Assert.AreEqual(3632m, point.Wind);
            Assert.AreEqual(29m, point.Solar);
            Assert.AreEqual(1272m, point.Oil);
            Assert.AreEqual(1434m, point.Other);
        }

        [Test]
        public void NetGenerationLessInterchangeApproximatesDemand()
        {
            // Sanity check on the column mapping itself: EIA-930 balances as
            // net generation - net interchange = demand. If two columns were transposed this breaks.
            var point = new EIAElectricity().Reader(Config("PJM"), SampleLine, DateTime.UtcNow, false) as EIAElectricity;

            var implied = point.NetGeneration.Value - point.TotalInterchange.Value;
            Assert.LessOrEqual(Math.Abs(point.Demand.Value - implied), 10m);
        }

        [Test]
        public void EndTimeIsOneHourAfterTime()
        {
            // The hour is published about an hour after it ends, so the bar close is also the
            // point-in-time correct delivery moment. LEAN fires the point at EndTime.
            var point = new EIAElectricity().Reader(Config("PJM"), SampleLine, DateTime.UtcNow, false) as EIAElectricity;

            Assert.AreEqual(new DateTime(2024, 1, 15, 1, 0, 0), point.EndTime);
            Assert.AreEqual(TimeSpan.FromHours(1), point.EndTime - point.Time);
        }

        [Test]
        public void SettingEndTimeShiftsTime()
        {
            // EndTime is derived, so its setter has to walk Time back by the period rather than
            // clobbering it.
            var point = new EIAElectricity { EndTime = new DateTime(2024, 1, 15, 1, 0, 0) };

            Assert.AreEqual(new DateTime(2024, 1, 15, 0, 0, 0), point.Time);
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
            var line = "20240115 00:00,113297,113196,121696,-8403,0,43401,33784,4144,0,29,,1272,1434,,-150,-42,,,,";
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
        public void FlagsAreCorrect()
        {
            var instance = new EIAElectricity();

            Assert.AreEqual(Resolution.Hour, instance.DefaultResolution());
            Assert.Contains(Resolution.Hour, instance.SupportedResolutions());
            Assert.IsFalse(instance.RequiresMapping());
            Assert.IsTrue(instance.IsSparseData());
            Assert.AreEqual(TimeZones.Utc, instance.DataTimeZone());
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
