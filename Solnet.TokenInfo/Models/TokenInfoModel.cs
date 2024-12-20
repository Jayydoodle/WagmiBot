using System;
using System.Numerics;

namespace Solnet.TokenInfo.Models
{
    /// <summary>
    /// Represents comprehensive information about a Solana token
    /// </summary>
    public class TokenInfoModel
    {
        /// <summary>
        /// The token's contract address
        /// </summary>
        public string ContractAddress { get; set; } = string.Empty;

        /// <summary>
        /// The token's name
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The token's symbol
        /// </summary>
        public string Symbol { get; set; } = string.Empty;

        /// <summary>
        /// The token's decimals
        /// </summary>
        public int Decimals { get; set; }

        /// <summary>
        /// Current price in SOL
        /// </summary>
        public decimal PriceInSol { get; set; }

        /// <summary>
        /// Current price in USD
        /// </summary>
        public decimal PriceInUsd { get; set; }

        /// <summary>
        /// Total market capitalization
        /// </summary>
        public decimal MarketCap { get; set; }

        public string MarketCapFormatted => $"{MarketCap:C}";

        /// <summary>
        /// Total trading volume in the last 24 hours
        /// </summary>
        public decimal Volume24H { get; set; }

        /// <summary>
        /// Total number of token holders
        /// </summary>
        public int NumberOfHolders { get; set; }

        /// <summary>
        /// Total supply of the token
        /// </summary>
        public BigInteger TotalSupply { get; set; }

        /// <summary>
        /// Circulating supply of the token
        /// </summary>
        public BigInteger CirculatingSupply { get; set; }
    }
}
