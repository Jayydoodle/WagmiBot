using System;
using System.Net.Http;
using System.Numerics;
using System.Threading.Tasks;
using Solnet.Rpc;
using Solnet.Rpc.Core.Http;
using Solnet.Rpc.Models;
using Solnet.TokenInfo.Models;
using Newtonsoft.Json.Linq;
using Solnet.JupiterSwap;
using Solnet.JupiterSwap.Models;

namespace Solnet.TokenInfo.Services
{
    /// <summary>
    /// Service for retrieving Solana token information
    /// </summary>
    public class SolanaTokenInfoService
    {
        private readonly IRpcClient _rpcClient;
        private readonly HttpClient _httpClient;
        private readonly string _rpcUrl;

        private static readonly Lazy<SolanaTokenInfoService> _instance = new Lazy<SolanaTokenInfoService>(() => new SolanaTokenInfoService());
        public static SolanaTokenInfoService Instance => _instance.Value;
        private JupiterDexAg JupiterDex { get; set; }

        /// <summary>
        /// Constructor for SolanaTokenInfoService
        /// </summary>
        /// <param name="rpcUrl">Solana RPC endpoint URL</param>
        public SolanaTokenInfoService(string rpcUrl = "https://api.mainnet-beta.solana.com")
        {
            _rpcUrl = rpcUrl;
            _rpcClient = ClientFactory.GetClient(_rpcUrl);
            _httpClient = new HttpClient();
            JupiterDex = new JupiterDexAg();
        }

        /// <summary>
        /// Fetch comprehensive token information by contract address
        /// </summary>
        /// <param name="contractAddress">Token contract address</param>
        /// <returns>Detailed token information</returns>
        public async Task<TokenInfoModel> GetTokenInfoAsync(string contractAddress)
        {
            var tokenInfo = new TokenInfoModel 
            { 
                ContractAddress = contractAddress 
            };

            // Fetch token metadata and supply
            await FetchTokenMetadataAsync(contractAddress, tokenInfo);
            await FetchTokenSupplyAsync(contractAddress, tokenInfo);

            // Fetch market data from Jupiter Aggregator or other sources
            await FetchMarketDataAsync(contractAddress, tokenInfo);

            return tokenInfo;
        }

        /// <summary>
        /// Fetch token metadata from Solana blockchain
        /// </summary>
        private async Task FetchTokenMetadataAsync(string contractAddress, TokenInfoModel tokenInfo)
        {
            try 
            {
                TokenData data = await JupiterDex.GetTokenByMint(contractAddress); 

                if(data != null)
                {
                    tokenInfo.Name = data.Name;
                    tokenInfo.Symbol = data.Symbol;
                    tokenInfo.Decimals = data.Decimals;
                }
            }
            catch (Exception ex)
            {
                // Log the error or handle it appropriately
                Console.WriteLine($"Error fetching token metadata: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetch token supply from Solana blockchain
        /// </summary>
        private async Task FetchTokenSupplyAsync(string contractAddress, TokenInfoModel tokenInfo)
        {
            try 
            {
                // Fetch token supply using Solana RPC
                var supplyResponse = await _rpcClient.GetTokenSupplyAsync(contractAddress);
                
                if (supplyResponse.WasSuccessful)
                {
                    // Parse the amount as a BigInteger
                    if (BigInteger.TryParse(supplyResponse.Result.Value.Amount.ToString(), out BigInteger totalSupply))
                    {
                        tokenInfo.TotalSupply = totalSupply;
                        tokenInfo.Decimals = supplyResponse.Result.Value.Decimals;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error or handle it appropriately
                Console.WriteLine($"Error fetching token supply: {ex.Message}");
            }
        }

        /// <summary>
        /// Fetch market data from external sources
        /// </summary>
        private async Task FetchMarketDataAsync(string contractAddress, TokenInfoModel tokenInfo)
        {
            try 
            {
                // Use Jupiter Aggregator API for market data
                var jupiterApiUrl = $"https://api.jup.ag/price/v2?ids={contractAddress}";
                
                var response = await _httpClient.GetAsync(jupiterApiUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var marketData = JObject.Parse(content);

                    // Extract market data (this is a simplified example)
                    if (marketData["data"][contractAddress] != null)
                    {
                        tokenInfo.PriceInUsd = marketData["data"][contractAddress]["price"]?.Value<decimal>() ?? 0;
                        //tokenInfo.Volume24H = marketData[contractAddress]["volume24h"]?.Value<decimal>() ?? 0;
                        
                        // Calculate market cap if price and total supply are available
                        if (tokenInfo.TotalSupply > 0)
                        {
                            // Convert total supply to a more manageable decimal representation
                            decimal totalSupplyDecimal = (decimal)BigInteger.Divide(tokenInfo.TotalSupply, BigInteger.Pow(10, tokenInfo.Decimals));
                            tokenInfo.MarketCap = tokenInfo.PriceInUsd * totalSupplyDecimal;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the error or handle it appropriately
                Console.WriteLine($"Error fetching market data: {ex.Message}");
            }
        }
    }
}
