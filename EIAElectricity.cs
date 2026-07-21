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
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NodaTime;
using QuantConnect.Data;
using QuantConnect.Util;

namespace QuantConnect.DataSource
{
    /// <summary>
    /// U.S. electric grid operating data from EIA Form EIA-930, one record per balancing authority
    /// per day: demand, the day-ahead demand forecast, net generation, net interchange with
    /// neighbouring authorities, and the generation split across all sixteen fuel types the form
    /// reports. Every value is in megawatthours. The daily totals use the Eastern day boundary.
    /// </summary>
    public class EIAElectricity : BaseData
    {
        /// <summary>
        /// End of the operating day, one day after the day the record covers. This is when LEAN
        /// delivers the data point, once EIA has published the completed day.
        /// </summary>
        public override DateTime EndTime => Time.AddDays(1);

        /// <summary>Actual electricity demand on the grid.</summary>
        public decimal? Demand { get; set; }

        /// <summary>Demand the balancing authority forecast a day ahead for this hour.</summary>
        public decimal? DemandForecast { get; set; }

        /// <summary>Total net generation. This is also the data point's Value.</summary>
        public decimal? NetGeneration { get; set; }

        /// <summary>Net interchange with neighbouring authorities. Positive exports, negative imports.</summary>
        public decimal? TotalInterchange { get; set; }

        /// <summary>Net generation from coal.</summary>
        public decimal? Coal { get; set; }

        /// <summary>Net generation from natural gas.</summary>
        public decimal? NaturalGas { get; set; }

        /// <summary>Net generation from nuclear.</summary>
        public decimal? Nuclear { get; set; }

        /// <summary>Net generation from hydro.</summary>
        public decimal? Hydro { get; set; }

        /// <summary>Net generation from wind.</summary>
        public decimal? Wind { get; set; }

        /// <summary>Net generation from solar.</summary>
        public decimal? Solar { get; set; }

        /// <summary>Net generation from geothermal.</summary>
        public decimal? Geothermal { get; set; }

        /// <summary>Net generation from petroleum.</summary>
        public decimal? Oil { get; set; }

        /// <summary>Net generation from sources the form groups as other.</summary>
        public decimal? Other { get; set; }

        /// <summary>Net generation the balancing authority did not classify.</summary>
        public decimal? Unknown { get; set; }

        /// <summary>Net generation from pumped storage. Negative while pumping.</summary>
        public decimal? PumpedStorage { get; set; }

        /// <summary>Net generation from battery storage. Negative while charging.</summary>
        public decimal? Battery { get; set; }

        /// <summary>Net generation from storage the form groups as other.</summary>
        public decimal? OtherStorage { get; set; }

        /// <summary>Net generation from storage the balancing authority did not classify.</summary>
        public decimal? UnknownStorage { get; set; }

        /// <summary>Net generation from solar paired with integrated battery storage.</summary>
        public decimal? SolarWithStorage { get; set; }

        /// <summary>Net generation from wind paired with integrated battery storage.</summary>
        public decimal? WindWithStorage { get; set; }

        /// <summary>
        /// The EIA-930 fuel type codes that feed each generation column, in the exact order the
        /// processor writes them (csv[5] onward) and the <see cref="EIAElectricity(string)"/> parser
        /// reads them back. Single source of truth for the column layout, kept next to the parser.
        /// </summary>
        public static IReadOnlyList<(string Column, string FuelType)> FuelColumns { get; } = new[]
        {
            ("coal", "COL"),
            ("naturalgas", "NG"),
            ("nuclear", "NUC"),
            ("hydro", "WAT"),
            ("wind", "WND"),
            ("solar", "SUN"),
            ("geothermal", "GEO"),
            ("oil", "OIL"),
            ("other", "OTH"),
            ("unknown", "UNK"),
            ("pumpedstorage", "PS"),
            ("battery", "BAT"),
            ("otherstorage", "OES"),
            ("unknownstorage", "UES"),
            ("solarwithstorage", "SNB"),
            ("windwithstorage", "WNB")
        };

        /// <summary>
        /// The region-data type codes that feed the four headline columns, in the order the processor
        /// writes them (csv[1] onward).
        /// </summary>
        public static IReadOnlyList<(string Column, string Type)> RegionColumns { get; } = new[]
        {
            ("demand", "D"),
            ("demandforecast", "DF"),
            ("netgeneration", "NG"),
            ("totalinterchange", "TI")
        };

        /// <summary>Default constructor required by LEAN.</summary>
        public EIAElectricity()
        {
        }

        /// <summary>Parses one CSV line into a data point.</summary>
        public EIAElectricity(string line)
        {
            var csv = line.Split(',');
            Time = DateTime.ParseExact(csv[0], "yyyyMMdd", CultureInfo.InvariantCulture);
            Demand = ParseCell(csv[1]);
            DemandForecast = ParseCell(csv[2]);
            NetGeneration = ParseCell(csv[3]);
            TotalInterchange = ParseCell(csv[4]);
            Coal = ParseCell(csv[5]);
            NaturalGas = ParseCell(csv[6]);
            Nuclear = ParseCell(csv[7]);
            Hydro = ParseCell(csv[8]);
            Wind = ParseCell(csv[9]);
            Solar = ParseCell(csv[10]);
            Geothermal = ParseCell(csv[11]);
            Oil = ParseCell(csv[12]);
            Other = ParseCell(csv[13]);
            Unknown = ParseCell(csv[14]);
            PumpedStorage = ParseCell(csv[15]);
            Battery = ParseCell(csv[16]);
            OtherStorage = ParseCell(csv[17]);
            UnknownStorage = ParseCell(csv[18]);
            SolarWithStorage = ParseCell(csv[19]);
            WindWithStorage = ParseCell(csv[20]);
            Value = NetGeneration ?? 0m;
        }

        private static decimal? ParseCell(string cell)
        {
            return cell.IfNotNullOrEmpty<decimal?>(s => decimal.Parse(s, NumberStyles.Any, CultureInfo.InvariantCulture));
        }

        /// <summary>Location of the source file: alternative/eia/electricity/{code}.csv</summary>
        public override SubscriptionDataSource GetSource(SubscriptionDataConfig config, DateTime date, bool isLiveMode)
        {
            return new SubscriptionDataSource(
                Path.Combine(
                    Globals.DataFolder,
                    "alternative",
                    "eia",
                    "electricity",
                    $"{config.Symbol.Value.ToLowerInvariant()}.csv"
                ),
                SubscriptionTransportMedium.LocalFile,
                FileFormat.Csv
            );
        }

        /// <summary>Parses the data from the line provided and loads it into LEAN.</summary>
        public override BaseData Reader(SubscriptionDataConfig config, string line, DateTime date, bool isLiveMode)
        {
            return new EIAElectricity(line) { Symbol = config.Symbol };
        }

        /// <summary>
        /// Data time zone. The daily EIA-930 totals use the Eastern day boundary, matching our other
        /// U.S. agency datasets (BEA, CFTC, FINRA).
        /// </summary>
        public override DateTimeZone DataTimeZone() => TimeZones.NewYork;

        /// <summary>Supported resolutions. Daily, the cadence the data fleet processes at.</summary>
        public override List<Resolution> SupportedResolutions() => DailyResolution;

        /// <summary>Default resolution.</summary>
        public override Resolution DefaultResolution() => Resolution.Daily;

        /// <summary>Sparse: one file per balancing authority, suppress missing-file logs.</summary>
        public override bool IsSparseData() => true;

        /// <summary>Unlinked (keyed by balancing authority, not a mapped equity).</summary>
        public override bool RequiresMapping() => false;

        /// <summary>Creates a copy of the instance.</summary>
        public override BaseData Clone()
        {
            return new EIAElectricity
            {
                Symbol = Symbol,
                Time = Time,
                Value = Value,
                Demand = Demand,
                DemandForecast = DemandForecast,
                NetGeneration = NetGeneration,
                TotalInterchange = TotalInterchange,
                Coal = Coal,
                NaturalGas = NaturalGas,
                Nuclear = Nuclear,
                Hydro = Hydro,
                Wind = Wind,
                Solar = Solar,
                Geothermal = Geothermal,
                Oil = Oil,
                Other = Other,
                Unknown = Unknown,
                PumpedStorage = PumpedStorage,
                Battery = Battery,
                OtherStorage = OtherStorage,
                UnknownStorage = UnknownStorage,
                SolarWithStorage = SolarWithStorage,
                WindWithStorage = WindWithStorage
            };
        }

        /// <summary>String representation for debugging.</summary>
        public override string ToString()
        {
            return $"{Symbol} - Demand: {Demand}, DemandForecast: {DemandForecast}, " +
                   $"NetGeneration: {NetGeneration}, TotalInterchange: {TotalInterchange}";
        }
    }
}
