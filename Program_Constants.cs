using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace analyzer_engine
{
    public partial class Program
    {
        /// <summary>
        /// The IEX Cloud Apperate API key
        /// </summary>
        public const string API_TOKEN = "{your_iex_cloud_api_key}";

        /// <summary>
        /// This value should be set to the 30-year Treasury bond yield minus the expected inflation rate.
        /// </summary>
        /// <remarks>The discount rate is the rate at which the value of a company's future cash flows is discounted to their present value.</remarks>
        public const decimal DISCOUNT_RATE = 0.08m;

        /// <summary>
        /// We will assume that all of our stocks have a minimal terminal growth rate of 2% per year. Otherwise, the business is dying.
        /// </summary>
        public const decimal TERMINAL_GROWTH_RATE = 0.05m;

        /// <summary>
        /// We don't want to use growth rates higher than 15% in calculations
        /// </summary>
        public static decimal MaxConservativeGrowthRate = 0.15m;

    }
}
