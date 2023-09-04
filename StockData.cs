using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace analyzer_engine
{
    public class StockData
    {
        #region Stock identifiers and categorization
        public string ErrorStatus { get; set; }

        public string Symbol { get; set; }
        public string Name { get; set; }
        public string Sector { get; set; }
        public string Industry { get; set; }
        #endregion

        #region Stock market daily quote data
        public decimal Price { get; set; }
        public decimal MarketCapitalization { get; set; }
        public decimal Volume { get; set; }

        public decimal SharesOutstanding { get; set; }
        #endregion


        public decimal Cash { get; set; }

        /// <summary>
        /// This number should always be a POSITIVE number or zero. There is no such thing (for our purposes) as a negative value for this number.
        /// </summary>
        public decimal Debt { get; set; }

        public decimal NetCash => Cash - Math.Abs(Debt);

        #region Annual free cash flow and the components
        public decimal EbitCurrentYear { get; set; }
        public decimal DepreciationCurrentYear { get; set; }
        public decimal CapitalExpendituresCurrentYear { get; set; }
        public decimal FreeCashflowCurrentYear { get; set; }
        #endregion


        #region Average free cash flow and the components
        public decimal Ebit3YrAverage { get; set; }
        public decimal Depreciation3YrAverage { get; set; }
        public decimal CapitalExpenditures3YrAverage { get; set; }
        public decimal FreeCashflow3YrAverage { get; set; }

        public decimal FreeCashflow3YrAveragePerShare => FreeCashflow3YrAverage / SharesOutstanding;
        #endregion

        /*
         * For now I'm going to compute a simplified discounted cash flow where I project out the growth over
         * the next 5-years and then just compute the terminal value from that.
         *
         * This will be close enough and much faster to program.
         */
        #region DCF calculation components
        public decimal FreeCashFlowAfter5YrsConservativeGrowth => (decimal)(FreeCashflow3YrAverage * (decimal)Math.Pow(1 + (double)RecentAverageFCFConservativeGrowthRate, 5));

        public decimal FreeCashFlowAfter5YrsAggressiveGrowth => (decimal)(FreeCashflowCurrentYear * (decimal)Math.Pow(1 + (double)RecentAverageFCFConservativeGrowthRate, 5));

        /// <summary>
        /// Use the change over the last three years divided by the number of changes (i.e. 2)
        /// </summary>
        public decimal RecentAverageFCFGrowthRate { get; set; }

        /// <summary>
        /// Divide <see cref="RecentAverageFCFGrowthRate"/> by two to avoid giving too much weight to recent changes in free cash flow
        /// </summary>
        public decimal RecentAverageFCFConservativeGrowthRate =>
            RecentAverageFCFGrowthRate > .30m //if the growth rate is higher than 30 percent, it's too agressive
                ? Program.MaxConservativeGrowthRate //return 15% as our max growth rate
                : (RecentAverageFCFGrowthRate > 0m) ? RecentAverageFCFGrowthRate / 2 : //Otherwise divide by 2 
                    RecentAverageFCFGrowthRate; //unless its decline rather than growth

        public decimal TerminalGrowthRate => Program.TERMINAL_GROWTH_RATE;
        public decimal DiscountRate => Program.DISCOUNT_RATE;

        /// <summary>
        /// For now we're ONLY using terminal value because it is faster to calculate than doing out the whole sum of two different growth rates short and near term
        /// </summary>
        /// <remarks>
        /// terminal value = (free cash flow * (1 + growth rate)) / (discount rate - growth rate)
        /// 
        /// Our algorithm will assume a growth rate of 2% and a discount rate of DISCOUNT_RATE above
        ///
        /// For now I'm going to compute a simplified discounted cash flow where I project out the growth over
        /// the next 5-years and then just compute the terminal value from that.
        /// 
        /// This will be close enough and much faster to program.
        /// </remarks>
        public decimal TerminalValue => (FreeCashFlowAfter5YrsConservativeGrowth * (1 + TerminalGrowthRate)) / (DiscountRate - TerminalGrowthRate);

        public decimal TerminalValueAggressiveGrowth => (FreeCashFlowAfter5YrsAggressiveGrowth * (1 + TerminalGrowthRate)) / (DiscountRate - TerminalGrowthRate);

        public decimal TerminalValueAdjustedForNetCash => (TerminalValue + NetCash);

        public decimal TerminalValueAggresiveGrowthAdjustedForNetCash => (TerminalValueAggressiveGrowth + NetCash);

        /// <summary>
        /// Adjusted for net debt
        /// </summary>
        public decimal DcfTargetPerSharePriceAdjustedForNetCash => (TerminalValueAdjustedForNetCash) / SharesOutstanding;

        public decimal DcfTargetPerSharePriceAggressiveGrowthAdjustedForNetCash => (TerminalValueAggresiveGrowthAdjustedForNetCash) / SharesOutstanding;
        #endregion

        #region Under/over valued calculations

        public bool IsUndervalued => TerminalValueAdjustedForNetCash > MarketCapitalization;

        /// <summary>
        /// The percentage your money would grow if this stock moved from its current market cap to it's true cashflow + net_cash value
        /// </summary>
        public decimal UpsidePotential => (TerminalValueAdjustedForNetCash / MarketCapitalization) - 1;
        #endregion

        #region Useful Yahoo Finance URLs
        public string YahooFinanceQuote => $"https://finance.yahoo.com/quote/{Symbol}";
        public string YahooFinanceIncomeAnnual => $"https://finance.yahoo.com/quote/{Symbol}/financials";
        public string YahooFinanceCashFlowAnnual => $"https://finance.yahoo.com/quote/{Symbol}/cash-flow";
        public string YahooFinanceBalanceSheetAnnual => $"https://finance.yahoo.com/quote/{Symbol}/balance-sheet";
        #endregion

        public static StockData FromJson(string json)
        {
            var tempDeserializeObject = JsonConvert.DeserializeObject<StockData>(json);
            return tempDeserializeObject;
        }

        public static string HeaderRow(bool addYahooFinanceLinks = false, bool allColumns = false)
        {
            string headerRow = String.Empty;
            StringBuilder sb = new StringBuilder();
            
            sb.Append($"{SplitCamelCase(nameof(UpsidePotential))},");
            sb.Append($"{SplitCamelCase(nameof(Symbol))},");
            sb.Append($"{SplitCamelCase(nameof(Name))},");

            if (allColumns)
            {
                sb.Append($"{SplitCamelCase(nameof(Sector))},");
                sb.Append($"{SplitCamelCase(nameof(Industry))},");
            }
            
            sb.Append($"{SplitCamelCase(nameof(Price))},");

            if (allColumns)
            {
                sb.Append($"{SplitCamelCase(nameof(SharesOutstanding), true)},");
                sb.Append($"{SplitCamelCase(nameof(Volume), true)},");
            }

            sb.Append($"{SplitCamelCase(nameof(MarketCapitalization), true)},");
            sb.Append($"{SplitCamelCase(nameof(Cash), true)},");
            sb.Append($"{SplitCamelCase(nameof(Debt), true)},");
            sb.Append($"{SplitCamelCase(nameof(NetCash), true)},");

            if (allColumns)
            {
                sb.Append($"{SplitCamelCase(nameof(EbitCurrentYear), true)},");
                sb.Append($"{SplitCamelCase(nameof(Ebit3YrAverage), true)},");
                sb.Append($"{SplitCamelCase(nameof(DepreciationCurrentYear), true)},");
                sb.Append($"{SplitCamelCase(nameof(Depreciation3YrAverage), true)},");
                sb.Append($"{SplitCamelCase(nameof(CapitalExpendituresCurrentYear), true)},");
                sb.Append($"{SplitCamelCase(nameof(CapitalExpenditures3YrAverage), true)},");
            }

            sb.Append($"{SplitCamelCase(nameof(FreeCashflowCurrentYear), true)},");
            sb.Append($"{SplitCamelCase(nameof(FreeCashflow3YrAverage), true)},");
            sb.Append($"{SplitCamelCase(nameof(FreeCashflow3YrAveragePerShare))},");
            sb.Append($"{SplitCamelCase(nameof(RecentAverageFCFGrowthRate))},");
            sb.Append($"{SplitCamelCase(nameof(RecentAverageFCFConservativeGrowthRate))},");
            sb.Append($"{SplitCamelCase(nameof(FreeCashFlowAfter5YrsConservativeGrowth))},");

            sb.Append($"{SplitCamelCase(nameof(FreeCashFlowAfter5YrsAggressiveGrowth))},");

            if (allColumns)
            {
                sb.Append($"{SplitCamelCase(nameof(TerminalGrowthRate))},");
                sb.Append($"{SplitCamelCase(nameof(DiscountRate))},");
            }
            
            sb.Append($"{SplitCamelCase(nameof(TerminalValue), true)},");
            sb.Append($"{SplitCamelCase(nameof(TerminalValueAggressiveGrowth), true)},");
            sb.Append($"{SplitCamelCase(nameof(TerminalValueAdjustedForNetCash), true)},");
            sb.Append($"{SplitCamelCase(nameof(TerminalValueAggresiveGrowthAdjustedForNetCash), true)},");
            sb.Append($"{SplitCamelCase(nameof(DcfTargetPerSharePriceAdjustedForNetCash))},");
            sb.Append($"{SplitCamelCase(nameof(DcfTargetPerSharePriceAggressiveGrowthAdjustedForNetCash))}");

            if (addYahooFinanceLinks)
            {
                sb.Append($",");
                sb.Append($"{SplitCamelCase(nameof(YahooFinanceQuote))},");
                sb.Append($"{SplitCamelCase(nameof(YahooFinanceIncomeAnnual))},");
                sb.Append($"{SplitCamelCase(nameof(YahooFinanceCashFlowAnnual))},");
                sb.Append($"{SplitCamelCase(nameof(YahooFinanceBalanceSheetAnnual))}");
            }

            headerRow = sb.ToString();
            return headerRow;
        }

        public string ToCsvRecord(bool addYahooFinanceLinks = false, bool allColumns = false)
        {
            if (!String.IsNullOrWhiteSpace(ErrorStatus))
            {
                Program.LogError(new[] { $"{Symbol}:{ErrorStatus}" });
            }

            string csvLine = String.Empty;
            StringBuilder sb = new StringBuilder();
            
            sb.Append($"\"{UpsidePotential:P0}\",");
            sb.Append($"{Symbol},");
            sb.Append($"{Name.Replace(",", " ")},");

            if (allColumns)
            {
                sb.Append($"{Sector},");
                sb.Append($"{Industry},");
            }
            
            sb.Append($"\"{Price:C2}\",");

            if (allColumns)
            {
                sb.Append($"\"{(SharesOutstanding / 1000):N0}\",");
                sb.Append($"\"{(Volume / 1000):N0}\",");
            }

            sb.Append($"\"{(MarketCapitalization / 1000):C0}\",");
            sb.Append($"\"{(Cash / 1000):C0}\",");
            sb.Append($"\"{(Debt / 1000):C0}\",");
            sb.Append($"\"{(NetCash / 1000):C0}\",");

            if (allColumns)
            {
                sb.Append($"\"{(EbitCurrentYear / 1000):C0}\",");
                sb.Append($"\"{(Ebit3YrAverage / 1000):C0}\",");
                sb.Append($"\"{(DepreciationCurrentYear / 1000):C0}\",");
                sb.Append($"\"{(Depreciation3YrAverage / 1000):C0}\",");
                sb.Append($"\"{(CapitalExpendituresCurrentYear / 1000):C0}\",");
                sb.Append($"\"{(CapitalExpenditures3YrAverage / 1000):C0}\",");
            }

            sb.Append($"\"{(FreeCashflowCurrentYear / 1000):C0}\",");
            sb.Append($"\"{(FreeCashflow3YrAverage / 1000):C0}\",");
            sb.Append($"\"{FreeCashflow3YrAveragePerShare:C2}\",");
            sb.Append($"\"{RecentAverageFCFGrowthRate:P2}\",");
            sb.Append($"\"{RecentAverageFCFConservativeGrowthRate:P2}\",");
            sb.Append($"\"{(FreeCashFlowAfter5YrsConservativeGrowth / 1000):C0}\",");
            sb.Append($"\"{(FreeCashFlowAfter5YrsAggressiveGrowth / 1000):C0}\",");

            if (allColumns)
            {
                sb.Append($"\"{TerminalGrowthRate:P0}\",");
                sb.Append($"\"{DiscountRate:P0}\",");
            }

            sb.Append($"\"{(TerminalValue / 1000):C0}\",");
            sb.Append($"\"{(TerminalValueAggressiveGrowth / 1000):C0}\",");
            sb.Append($"\"{(TerminalValueAdjustedForNetCash / 1000):C0}\",");
            sb.Append($"\"{(TerminalValueAggresiveGrowthAdjustedForNetCash / 1000):C0}\",");
            sb.Append($"\"{DcfTargetPerSharePriceAdjustedForNetCash:C2}\",");
            sb.Append($"\"{DcfTargetPerSharePriceAggressiveGrowthAdjustedForNetCash:C2}\"");

            if (addYahooFinanceLinks)
            {
                sb.Append($",");
                sb.Append($"{YahooFinanceQuote},");
                sb.Append($"{YahooFinanceIncomeAnnual},");
                sb.Append($"{YahooFinanceCashFlowAnnual},");
                sb.Append($"{YahooFinanceBalanceSheetAnnual}");
            }
            

            csvLine = sb.ToString();
            return csvLine;
        }

        public static string SplitCamelCase(string input, bool thousandsIndicator = false)
        {
            return System.Text.RegularExpressions.Regex.Replace(input, "([A-Z])", " $1", System.Text.RegularExpressions.RegexOptions.Compiled).Trim() + (thousandsIndicator ? " (000s)" : "");
        }
    }
}
