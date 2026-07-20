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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QuantConnect.Configuration;
using QuantConnect.DataSource;
using QuantConnect.Logging;
using QuantConnect.Util;

namespace QuantConnect.DataProcessing
{
    /// <summary>
    /// Downloads U.S. electric grid operating data (Form EIA-930) from the EIA API v2 and converts it
    /// to LEAN's per-balancing-authority CSV format. Each balancing authority gets one wide row per
    /// hour combining the four region-data metrics with the generation split across all sixteen fuel
    /// types, since both routes share the same key (respondent, period) and the same hourly cadence.
    ///
    /// Output: {destination}/electricity/{balancingauthority}.csv
    /// Row layout: time,&lt;4 region columns&gt;,&lt;16 fuel columns&gt;
    /// (see EIAElectricity.RegionColumns and EIAElectricity.FuelColumns for the order)
    ///
    /// Time is the operating hour in UTC. There is no endtime column: EndTime is Time + 1 hour, which
    /// is both the end of the hourly bar and the measured EIA-930 publication lag.
    ///
    /// Full-history mode (no QC_DATAFLEET_DEPLOYMENT_DATE): every hour from 2019 to now, walked one
    /// balancing authority and one year at a time so a failure retries a small window instead of the
    /// whole run. Incremental mode (QC_DATAFLEET_DEPLOYMENT_DATE set): just that day, merged
    /// idempotently into the published history.
    /// </summary>
    public class EIAElectricityDownloader : IDisposable
    {
        /// <summary>Vendor name (matches the alternative/&lt;vendor&gt; data path).</summary>
        public static string VendorName => "eia";

        /// <summary>Dataset name (matches the alternative/&lt;vendor&gt;/&lt;dataset&gt; data path).</summary>
        public static string VendorDataName => "electricity";

        private const string ApiBaseUrl = "https://api.eia.gov/v2/electricity/rto";
        private const string RegionRoute = "region-data";
        private const string FuelRoute = "fuel-type-data";
        // The API caps a JSON response at 5000 rows and warns when the result is truncated.
        private const int PageSize = 5000;
        // Attempts per request before giving up on a balancing authority.
        private const int MaxAttempts = 5;
        // EIA-930 hourly history starts in mid-2018; 2018 is partial, so we walk from its first full year.
        private const int FirstYear = 2019;

        private readonly string _destinationDirectory;
        private readonly string _processedDataDirectory;
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        // EIA does not publish a hard rate limit; stay polite, this is a long backfill.
        private readonly RateGate _rateGate = new RateGate(60, TimeSpan.FromMinutes(1));

        // Column layout lives on the data-model class, next to the Reader — the single source of truth.
        // Region metrics occupy the first slots of each row, the fuel types follow.
        private static readonly IReadOnlyList<(string Column, string Type)> RegionColumns = EIAElectricity.RegionColumns;
        private static readonly IReadOnlyList<(string Column, string FuelType)> FuelColumns = EIAElectricity.FuelColumns;
        private static readonly int ColumnCount = RegionColumns.Count + FuelColumns.Count;

        // Discriminator value -> row slot, used to demultiplex a route's rows into their columns.
        private static readonly IReadOnlyDictionary<string, int> RegionColumnIndex =
            RegionColumns.Select((x, i) => (x.Type, i)).ToDictionary(x => x.Type, x => x.i);
        private static readonly IReadOnlyDictionary<string, int> FuelColumnIndex =
            FuelColumns.Select((x, i) => (x.FuelType, i)).ToDictionary(x => x.FuelType, x => RegionColumns.Count + x.i);

        /// <summary>
        /// Creates a new instance writing to <paramref name="destinationDirectory"/>, merging with any
        /// previously processed data found in <paramref name="processedDataDirectory"/>.
        /// </summary>
        public EIAElectricityDownloader(string destinationDirectory, string processedDataDirectory)
        {
            _destinationDirectory = destinationDirectory;
            _processedDataDirectory = processedDataDirectory;
            _apiKey = Config.Get("eia-api-key");
            if (string.IsNullOrEmpty(_apiKey))
            {
                throw new ArgumentException("eia-api-key is not set in config.json");
            }
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        }

        /// <summary>Runs the download/convert. Returns true on success.</summary>
        public bool Run()
        {
            try
            {
                var deploymentDateStr = Environment.GetEnvironmentVariable("QC_DATAFLEET_DEPLOYMENT_DATE");
                DateTime start;
                DateTime end;
                if (!string.IsNullOrEmpty(deploymentDateStr))
                {
                    // Incremental: just the deployment day.
                    start = DateTime.ParseExact(deploymentDateStr, "yyyyMMdd", CultureInfo.InvariantCulture);
                    end = start.AddDays(1);
                    Log.Trace($"EIAElectricityDownloader.Run(): incremental mode for {start:yyyy-MM-dd}");
                }
                else
                {
                    start = new DateTime(FirstYear, 1, 1);
                    end = DateTime.UtcNow.Date.AddDays(1);
                    Log.Trace($"EIAElectricityDownloader.Run(): full-history mode from {start:yyyy-MM-dd}");
                }

                var respondents = FetchRespondents();
                Log.Trace($"EIAElectricityDownloader.Run(): {respondents.Count} balancing authorities");

                var written = 0;
                var failed = new List<string>();
                foreach (var respondent in respondents)
                {
                    // One authority failing must not throw away the hours already downloaded for the
                    // other eighty. Collect the names and report them at the end so the run can be
                    // repeated for just those: the writer merges, so a rerun costs only what failed.
                    try
                    {
                        if (ProcessRespondent(respondent, start, end))
                        {
                            written++;
                        }
                    }
                    catch (Exception err)
                    {
                        Log.Error(err, $"EIAElectricityDownloader.Run(): {respondent} failed");
                        failed.Add(respondent);
                    }
                }

                Log.Trace($"EIAElectricityDownloader.Run(): {written} balancing authorities written");
                if (failed.Count > 0)
                {
                    Log.Error($"EIAElectricityDownloader.Run(): {failed.Count} failed: {string.Join(", ", failed)}");
                    return false;
                }
                return true;
            }
            catch (Exception err)
            {
                Log.Error(err, "EIAElectricityDownloader.Run(): failed");
                return false;
            }
        }

        /// <summary>
        /// Fetches every hour for one balancing authority, aligns both routes by period into one wide
        /// row, and merges the result into that authority's CSV.
        /// </summary>
        private bool ProcessRespondent(string respondent, DateTime start, DateTime end)
        {
            // grid[period] = one value slot per column.
            var grid = new Dictionary<DateTime, string[]>();

            // Walk a quarter at a time. A full-year window is what the API answers with a 503, and a
            // narrower window also bounds the offset paging and makes a retry cheap.
            for (var chunkStart = new DateTime(start.Year, ((start.Month - 1) / 3) * 3 + 1, 1);
                 chunkStart < end;
                 chunkStart = chunkStart.AddMonths(3))
            {
                var windowStart = chunkStart < start ? start : chunkStart;
                var windowEnd = chunkStart.AddMonths(3);
                if (windowEnd > end)
                {
                    windowEnd = end;
                }
                if (windowStart >= windowEnd)
                {
                    continue;
                }

                Collect(grid, RegionRoute, "type", RegionColumnIndex, respondent, windowStart, windowEnd);
                Collect(grid, FuelRoute, "fueltype", FuelColumnIndex, respondent, windowStart, windowEnd);
            }

            var rows = new List<string>(grid.Count);
            foreach (var (period, values) in grid)
            {
                // Emit a row when the authority reported at least one series for this hour. Small
                // authorities report demand but no fuel split, and storage codes only exist in recent
                // years, so gating on any single column would silently drop them.
                if (Array.TrueForAll(values, string.IsNullOrEmpty))
                {
                    continue;
                }

                var line = new List<string>(ColumnCount + 1)
                {
                    period.ToString("yyyyMMdd HH:mm", CultureInfo.InvariantCulture)
                };
                line.AddRange(values.Select(v => v ?? string.Empty));
                rows.Add(string.Join(",", line));
            }

            if (rows.Count == 0)
            {
                return false;
            }

            MergeAndWrite(respondent.ToLowerInvariant(), rows);
            return true;
        }

        /// <summary>
        /// Pages one route for one balancing authority and demultiplexes the rows into their columns by
        /// the route's discriminator field. Asking for every series at once instead of one request per
        /// series is what keeps the small authorities cheap: they have a handful of rows per quarter,
        /// which used to cost twenty near-empty requests and now costs one.
        /// </summary>
        private void Collect(Dictionary<DateTime, string[]> grid, string route, string field,
            IReadOnlyDictionary<string, int> columns, string respondent, DateTime start, DateTime end)
        {
            var offset = 0;
            while (true)
            {
                var rows = FetchPage(route, respondent, start, end, offset);
                if (rows.Count == 0)
                {
                    return;
                }

                foreach (var row in rows)
                {
                    if (!row.TryGetProperty("period", out var periodProp) ||
                        !TryParsePeriod(periodProp.GetString(), out var period) ||
                        !row.TryGetProperty(field, out var fieldProp) ||
                        !columns.TryGetValue(fieldProp.GetString() ?? string.Empty, out var column))
                    {
                        continue;
                    }

                    if (!grid.TryGetValue(period, out var values))
                    {
                        values = new string[ColumnCount];
                        grid[period] = values;
                    }
                    values[column] = SanitizeValue(row.TryGetProperty("value", out var v) ? ReadValue(v) : null);
                }

                if (rows.Count < PageSize)
                {
                    return;
                }
                offset += PageSize;
            }
        }

        /// <summary>Fetches one page of a route, retrying the transient failures a long backfill will hit.</summary>
        private List<JsonElement> FetchPage(string route, string respondent, DateTime start, DateTime end, int offset)
        {
            var url = $"{ApiBaseUrl}/{route}/data/?api_key={_apiKey}&frequency=hourly&data[0]=value" +
                      $"&facets[respondent][]={respondent}" +
                      $"&start={start:yyyy-MM-dd}T00&end={end:yyyy-MM-dd}T00" +
                      $"&sort[0][column]=period&sort[0][direction]=asc" +
                      $"&offset={offset}&length={PageSize}";

            var json = GetWithRetry(url);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("response", out var response) ||
                !response.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                return new List<JsonElement>();
            }

            var elements = new List<JsonElement>();
            foreach (var element in data.EnumerateArray())
            {
                elements.Add(element.Clone());
            }
            return elements;
        }

        /// <summary>
        /// Issues one request, retrying on the transient failures the API throws under load. EIA answers
        /// a wide query with a 503 often enough that a backfill without this never finishes.
        /// </summary>
        private string GetWithRetry(string url)
        {
            for (var attempt = 1; ; attempt++)
            {
                _rateGate.WaitToProceed();
                try
                {
                    return _httpClient.GetStringAsync(url).GetAwaiter().GetResult();
                }
                catch (Exception err) when (attempt < MaxAttempts && IsTransient(err))
                {
                    // Back off exponentially: 4s, 16s, 64s, 256s.
                    var delay = TimeSpan.FromSeconds(Math.Pow(4, attempt));
                    Log.Trace($"EIAElectricityDownloader.GetWithRetry(): attempt {attempt} failed " +
                              $"({err.GetType().Name}), retrying in {delay.TotalSeconds:F0}s");
                    Thread.Sleep(delay);
                }
            }
        }

        /// <summary>True for the failures worth retrying: server overload, throttling and timeouts.</summary>
        private static bool IsTransient(Exception err)
        {
            if (err is TaskCanceledException)
            {
                return true;
            }

            if (err is not HttpRequestException http)
            {
                return false;
            }

            return http.StatusCode is null
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.BadGateway
                or HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError;
        }

        /// <summary>
        /// Fetches the balancing authority codes from the live respondent facet, so a new authority is
        /// picked up without a code change.
        /// </summary>
        private List<string> FetchRespondents()
        {
            var url = $"{ApiBaseUrl}/{RegionRoute}/facet/respondent/?api_key={_apiKey}";
            var json = GetWithRetry(url);
            using var doc = JsonDocument.Parse(json);

            var respondents = new List<string>();
            if (doc.RootElement.TryGetProperty("response", out var response) &&
                response.TryGetProperty("facets", out var facets))
            {
                foreach (var facet in facets.EnumerateArray())
                {
                    if (facet.TryGetProperty("id", out var id))
                    {
                        respondents.Add(id.GetString());
                    }
                }
            }

            if (respondents.Count == 0)
            {
                throw new InvalidOperationException("the respondent facet returned no balancing authorities");
            }

            return respondents.OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        /// <summary>Parses an EIA hourly period ("2026-07-20T17") into a UTC operating hour.</summary>
        private static bool TryParsePeriod(string raw, out DateTime period)
        {
            return DateTime.TryParseExact(raw, "yyyy-MM-ddTHH", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out period);
        }

        /// <summary>Reads a value cell, which the API returns as a JSON string or number depending on the route.</summary>
        private static string ReadValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                _ => null
            };
        }

        /// <summary>Cleans a raw value: returns empty for anything non-numeric so it parses back as null.</summary>
        private static string SanitizeValue(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            var cleaned = raw.Replace(",", "").Trim();
            return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
                ? cleaned
                : string.Empty;
        }

        /// <summary>
        /// Idempotent merge: union the new rows with whatever we already have (the previously processed
        /// file plus anything written earlier this run), sort by the time column, write atomically.
        /// The temp output starts empty each run, so an incremental run must pull in the published
        /// history from the processed-data directory or it would drop it.
        /// </summary>
        private void MergeAndWrite(string balancingAuthority, List<string> newRows)
        {
            var dir = Path.Combine(_destinationDirectory, VendorDataName);
            Directory.CreateDirectory(dir);
            var outPath = Path.Combine(dir, $"{balancingAuthority}.csv");

            var rows = new HashSet<string>(newRows);
            var processedPath = Path.Combine(_processedDataDirectory, VendorDataName, $"{balancingAuthority}.csv");
            foreach (var path in new[] { processedPath, outPath })
            {
                if (File.Exists(path))
                {
                    rows.UnionWith(File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l)));
                }
            }

            var sorted = rows.OrderBy(l => l.Split(',')[0], StringComparer.Ordinal).ToList();

            var tempPath = outPath + ".tmp";
            File.WriteAllLines(tempPath, sorted);
            File.Move(tempPath, outPath, overwrite: true);
        }

        /// <summary>Disposes unmanaged resources.</summary>
        public void Dispose()
        {
            _httpClient?.DisposeSafely();
            _rateGate?.DisposeSafely();
        }
    }
}
