using System.Reflection.Metadata.Ecma335;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace analyzer_engine
{
    public partial class Program
    {
        /// <summary>
        /// Log an error to the errors.log file
        /// </summary>
        /// <param name="logLines"></param>
        public static void LogError(string[] logLines)
        {
            foreach(var line in logLines) 
                Console.WriteLine(line);
            
            File.AppendAllLines("errors.log", logLines);
        }

        /// <summary>
        /// Log an error to the errors.log file
        /// </summary>
        /// <param name="error"></param>
        public static void LogError(string error)
        {
            Console.WriteLine(error);
            File.AppendAllLines("errors.log", new[] { error });
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            #region Display usage
            if (args.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Please provide a path to a JSON file that is an array of objects, with at least two fields -- Symbol and Name -- for each record in the array.");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("Example from the Debug\\net6.0 folder:");
                Console.WriteLine();
                Console.WriteLine("  .\\analyzer-engine.exe ..\\..\\..\\..\\out.csv [max_growth_rate (e.g. .15=15%)]");
                Console.WriteLine();
                Environment.Exit(1);
            }
            #endregion

            var today = DateTime.Now;
            var date = today.ToString("yyyy-MM-dd");
            var outputCsvFilenameTempalte = $"{date}-data{{0}}.csv";
            var json = File.ReadAllText(args[0]);
            var stockDataArray = JsonConvert.DeserializeObject<StockData[]>(json);

            if (args.Length > 1)
            {
                MaxConservativeGrowthRate = Convert.ToDecimal(args[1]);
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Max Conservative Growth Rate: {MaxConservativeGrowthRate:P2}");
            Console.ResetColor();
            
            StringBuilder sb = new();
            
            //append header row
            sb.AppendLine(StockData.HeaderRow());
            
            foreach (var stockData in stockDataArray)
            {
                var tickerSymbol = stockData.Symbol;
                Console.WriteLine($"Processing {tickerSymbol}...");

                try
                {
                    startagain:
                    #region Retrieve basic stock quote
                    HttpClient
                        client = new(); //see: https://learn.microsoft.com/en-us/dotnet/csharp/tutorials/console-webapiclient

                    //grab the json of the basic stock quote
                    var response = client
                        .GetAsync($"https://api.iex.cloud/v1/data/core/quote/{tickerSymbol}?token={API_TOKEN}").Result;
                    var stockQuoteJson = response.Content.ReadAsStringAsync().Result;

                    if (stockQuoteJson == "Too many requests")
                    {
                        Thread.Sleep(1000);
                        goto startagain;
                    }

                    Thread.Sleep(50);

                    Console.WriteLine(stockQuoteJson);
                    dynamic stockQuote = JsonConvert.DeserializeObject(stockQuoteJson);

                    if (stockQuote[0].close == null && stockQuote[0].iexClose == null && stockQuote[0].latestPrice == null)
                    {
                        LogError($"No quote data found for {tickerSymbol}...");
                        continue;
                    }
                    #endregion

                    #region Populate stock quote related data
                    stockData.Price = (decimal)(stockQuote[0].close?.Value ?? stockQuote[0].iexClose?.Value ?? stockQuote[0].latestPrice?.Value);
                    stockData.Volume = (decimal)stockQuote[0].avgTotalVolume;

                    if (stockData.Price == 0)
                    {
                        LogError($"{tickerSymbol}: Price is zero... Skipping...");
                        continue;
                    }

                    stockData.MarketCapitalization = (decimal)stockQuote[0].marketCap;

                    if (stockData.MarketCapitalization == 0)
                    {
                        LogError($"{tickerSymbol}: Market cap is zero... Skipping...");
                        continue;
                    }

                    //compute market capitalization
                    stockData.SharesOutstanding = stockData.MarketCapitalization / stockData.Price;

                    if (stockData.SharesOutstanding == 0)
                    {
                        LogError($"{tickerSymbol}: Share count is zero... Skipping...");
                        continue;
                    }
                    #endregion

                    #region Retrieve financials related data, except cap ex

                    int numberOfQuartersToPull = 12;

                    
                    response = client
                        .GetAsync(
                            $"https://api.iex.cloud/v1/data/CORE/FUNDAMENTALS/{tickerSymbol}/quarterly?last={numberOfQuartersToPull}&token={API_TOKEN}")
                        .Result;

                    
                    var financialsJson = response.Content.ReadAsStringAsync().Result;
                    if (financialsJson == "Too many requests")
                    {
                        Thread.Sleep(1000);
                        goto startagain;
                    }

                    Thread.Sleep(50);

                    dynamic financials = JsonConvert.DeserializeObject(financialsJson);

                    if (financials.Count < numberOfQuartersToPull)
                    {
                        LogError($"{tickerSymbol} has less than 3 years of financial data, skipping...");
                        continue;
                    }

                    #endregion
                    
                    #region Populate financials related data, except cap ex
                    //compute total cash vs total debt
                    stockData.Cash = (decimal)financials[0].assetsCurrentCash + (decimal)financials[0].cashLongTerm + (decimal)financials[0].cashOperating;

                    //wrapping each term in an absolute value to ensure debt terms are strictly positive (since later we do Cash - Debt. We don't want a double negative to turn into a positive.
                    stockData.Debt = Math.Abs((decimal)financials[0].liabilitiesNonCurrentDebt) + Math.Abs((decimal)financials[0].debtShortTerm) + Math.Abs((decimal)financials[0].debtFinancial);

                    if (stockData.Cash == 0)
                    {
                        LogError($"{tickerSymbol}: Cash is zero... Skipping...");
                        continue;
                    }

                    if (stockData.Debt < 0)
                    {
                        LogError($"{tickerSymbol}: Debt is negative... Check code logic...");
                    }

                    //get earnings before interest
                    var totalEbit3Years =
                        (decimal)((IEnumerable<dynamic>)financials).Sum(
                            (Func<dynamic, decimal>)(x => (decimal)x.ebitReported));

                    if (totalEbit3Years == 0)
                    {
                        LogError($"{tickerSymbol}: {nameof(totalEbit3Years)} is zero... Skipping...");
                        continue;
                    }
                    
                    stockData.Ebit3YrAverage = totalEbit3Years / 3;
                    stockData.EbitCurrentYear = (decimal)((IEnumerable<dynamic>)financials).Skip(0).Take(4)
                        .Sum((Func<dynamic, decimal>)(x => (decimal)x.ebitReported));

                    var ebitYear0 = (decimal)((IEnumerable<dynamic>)financials).Skip(8).Take(4)
                        .Sum((Func<dynamic, decimal>)(x => (decimal)x.ebitReported));
                    var ebitYear1 = (decimal)((IEnumerable<dynamic>)financials).Skip(4).Take(4)
                        .Sum((Func<dynamic, decimal>)(x => (decimal)x.ebitReported));
                    var ebitYear2 = stockData.EbitCurrentYear;

                    //get depreciation
                    var totalDepreciation =
                        (decimal)((IEnumerable<dynamic>)financials).Sum(
                            (Func<dynamic, decimal>)
                                (x => Math.Abs((decimal)x.depreciationAndAmortizationCashFlow) + Math.Abs((decimal)x.expensesDepreciationAndAmortization))
                            );

                    if (totalDepreciation == 0)
                    {
                        LogError($"{tickerSymbol}: {nameof(totalDepreciation)} is zero... Skipping...");
                        continue;
                    }

                    if (totalDepreciation < 0)
                    {
                        LogError($"{tickerSymbol}: Depreciation is negative... Check code logic...");
                    }

                    stockData.Depreciation3YrAverage = totalDepreciation / 3;
                    stockData.DepreciationCurrentYear = (decimal)((IEnumerable<dynamic>)financials).Skip(0).Take(4)
                        .Sum((Func<dynamic, decimal>)(x => Math.Abs((decimal)x.depreciationAndAmortizationCashFlow)));

                    var depreciationYear0 = (decimal)((IEnumerable<dynamic>)financials).Skip(8).Take(4)
                        .Sum((Func<dynamic, decimal>)(x => Math.Abs((decimal)x.depreciationAndAmortizationCashFlow)));
                    var depreciationYear1 = (decimal)((IEnumerable<dynamic>)financials).Skip(4).Take(4)
                        .Sum((Func<dynamic, decimal>)(x => Math.Abs((decimal)x.depreciationAndAmortizationCashFlow)));
                    var depreciationYear2 = Math.Abs(stockData.DepreciationCurrentYear);

                    #endregion

                    #region Retrieve cap ex data

                    response = client
                        .GetAsync(
                            $"https://api.iex.cloud/v1/data/CORE/CASH_FLOW/{tickerSymbol}/quarterly?last={numberOfQuartersToPull}&token={API_TOKEN}")
                        .Result;
                    
                    var cashFlowJson = response.Content.ReadAsStringAsync().Result;
                    if (cashFlowJson == "Too many requests")
                    {
                        Thread.Sleep(1000);
                        goto startagain;
                    }

                    Thread.Sleep(50);

                    dynamic cashFlowData = JsonConvert.DeserializeObject(cashFlowJson);

                    if (cashFlowData.Count < numberOfQuartersToPull)
                    {
                        Console.WriteLine($"{tickerSymbol} has less than 3 years of cash flow data, skipping...");
                        continue;
                    }

                    #endregion

                    #region Populate cap ex data

                    //get capex
                    var totalCapEx =
                        (decimal)((IEnumerable<dynamic>)cashFlowData).Sum(
                            (Func<dynamic, decimal>)(x => Math.Abs((decimal)x.capitalExpenditures)));

                    if (totalCapEx == 0)
                    {
                        LogError($"{tickerSymbol}: {nameof(totalCapEx)} is zero... Skipping...");
                        continue;
                    }

                    if (totalCapEx < 0)
                    {
                        LogError($"{tickerSymbol}: {nameof(totalCapEx)} is negative... Check code logic...");
                    }

                    stockData.CapitalExpenditures3YrAverage = totalCapEx / 3;
                    stockData.CapitalExpendituresCurrentYear = (decimal)((IEnumerable<dynamic>)cashFlowData).Skip(0)
                        .Take(4).Sum((Func<dynamic, decimal>)(x => Math.Abs((decimal)x.capitalExpenditures)));

                    var capExYear0 = (decimal)((IEnumerable<dynamic>)cashFlowData).Skip(8).Take(4)
                        .Sum((Func<dynamic, decimal>)(x => Math.Abs((decimal)x.capitalExpenditures)));
                    var capExYear1 = (decimal)((IEnumerable<dynamic>)cashFlowData).Skip(4).Take(4)
                        .Sum((Func<dynamic, decimal>)(x => Math.Abs((decimal)x.capitalExpenditures)));
                    var capExYear2 = stockData.CapitalExpendituresCurrentYear;

                    #endregion

                    #region Calculate free cash flow

                    /* earnings before income and taxes - depreciation + capex = free cash flow

                        Normally, one would add depreciation and subtract capex. However, what I don't like
                    about that is that capex can be discretionary. So, it shouldn't count against companies
                    that are investing in growth.

                    Whereas, it's depreciation that is a truer reflection of what the company needs to spend to
                    avoid deterioration.

                    Therefore, I'd rather credit capex back to the company and subtract out depreciation instead.
                     */
                    stockData.FreeCashflow3YrAverage = (stockData.Ebit3YrAverage - stockData.Depreciation3YrAverage + stockData.CapitalExpenditures3YrAverage) / 3;
                    stockData.FreeCashflowCurrentYear = (stockData.EbitCurrentYear - stockData.DepreciationCurrentYear + stockData.CapitalExpendituresCurrentYear);

                    var fcfYear0 = ebitYear0 - depreciationYear0 + capExYear0; //year minus two
                    var fcfYear1 = ebitYear1 - depreciationYear1 + capExYear1; //year minus one
                    var fcfYear2 = ebitYear2 - depreciationYear2 + capExYear2; //current year

                    #endregion

                    #region Calculate average growth

                    var fcfGrowthRateYear1 = (fcfYear1 / fcfYear0) - 1;
                    var fcfGrowthRateYear2 = (fcfYear2 / fcfYear1) - 1;
                    var averageGrowthRate = (fcfGrowthRateYear1 + fcfGrowthRateYear2) / 2;

                    stockData.RecentAverageFCFGrowthRate = averageGrowthRate;

                    #endregion

                    /* NOTE: DCF is computed in the expression bodied property StockData.TerminalValue.
                    
                    We're only using terminal value and ignoring manually summing the first 5 years. 
                    Otherwise, it's a lot of extra work for little less accuracy. */
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(ex);
                    Console.WriteLine($"An error occurred while processing {tickerSymbol}, see error above. Moving to next stock...");
                    Console.ResetColor();

                    Thread.Sleep(250);
                    LogError($"{stockData.Symbol}: {ex.Message}");
                    continue;
                }

                sb.AppendLine(stockData.ToCsvRecord());
            }

            bool saved = false;
            int attempt = 0;
            while (!saved)
            {
                try
                {
                    var filename = String.Format(outputCsvFilenameTempalte, attempt == 0 ? "" : attempt.ToString());
                    File.WriteAllText(filename, sb.ToString());
                    saved = true;
                    Console.WriteLine("Saved to " + filename);
                }
                catch (Exception ex)
                {
                    attempt++;
                    Console.WriteLine($"Error saving file: {ex.Message}. Will try again...");
                    Thread.Sleep(1000);
                }
            }
        }
    }
}