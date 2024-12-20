using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Solnet.JupiterSwap.Json;
using Solnet.JupiterSwap.Models;
using Solnet.JupiterSwap.Types;
using Solnet.Rpc.Models;
using Solnet.Wallet;
using System.Numerics;
using System.Text;

namespace Solnet.JupiterSwap
{
    /// <summary>
    /// Concrete implementation of IDexAggregator for Jupiter Aggregator. 
    /// </summary>
    public class JupiterDexAg : IDexAggregator
    {
        private readonly PublicKey? _account;
        private readonly string _endpoint;
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerSettings _serializerOptions;
        private List<TokenData>? _tokens;

        private static readonly Lazy<JupiterDexAg> _instance = new Lazy<JupiterDexAg>(() => new JupiterDexAg());
        public static JupiterDexAg Instance => _instance.Value;

        /// <summary>
        /// Public constructor; Create the JupiterDexAg instance with the account to use for the aggregator. 
        /// </summary>
        /// <param name="endpoint"></param>
        public JupiterDexAg(string endpoint = "https://quote-api.jup.ag/v6")
        {
            _endpoint = endpoint;
            _httpClient = new HttpClient();
            _serializerOptions = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters =
            {
                new EncodingConverter(),
                new StringEnumConverter()
            },
                NullValueHandling = NullValueHandling.Ignore
            };
        }

        /// <summary>
        /// Public constructor; Create the JupiterDexAg instance with the account to use for the aggregator. 
        /// </summary>
        /// <param name="account"></param>
        /// <param name="endpoint"></param>
        public JupiterDexAg(PublicKey account, string endpoint = "https://quote-api.jup.ag/v6")
        {
            _account = account;
            _endpoint = endpoint;
            _httpClient = new HttpClient();
            _serializerOptions = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters =
            {
                new EncodingConverter(),
                new StringEnumConverter()
            },
                NullValueHandling = NullValueHandling.Ignore
            };
        }

        /// <inheritdoc />
        public async Task<SwapQuoteAg> GetSwapQuote(
            PublicKey inputMint,
            PublicKey outputMint,
            BigInteger amount,
            SwapMode swapMode = SwapMode.ExactIn,
            ushort? slippageBps = null,
            List<string>? excludeDexes = null,
            bool onlyDirectRoutes = false,
            ushort? platformFeeBps = null,
            ushort? maxAccounts = null)
        {
            // Construct the query parameters
            List<KeyValuePair<string, string>> queryParams = new()
        {
            new("inputMint", inputMint.ToString()),
            new("outputMint", outputMint.ToString()),
            new("amount", amount.ToString()),
            new("swapMode", swapMode.ToString()),
            new("asLegacyTransaction", "true")
        };

            var queryString = string.Join("&", queryParams.Select(kv => $"{kv.Key}={kv.Value}"));

            // Construct the request URL
            var apiUrl = _endpoint + "/quote?" + queryString;

            using var httpReq = new HttpRequestMessage(HttpMethod.Get, apiUrl);

            // execute the REST request
            using var response = await _httpClient.SendAsync(httpReq);

            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                // Deserialize the response JSON into SwapQuoteAg object
                SwapQuoteAg? swapQuote = JsonConvert.DeserializeObject<SwapQuoteAg>(responseBody, _serializerOptions);
                if (swapQuote != null)
                    return swapQuote;
            }

            // Handle error scenarios
            throw new HttpRequestException($"Request failed with status code: {response.StatusCode}");
        }

        /// <inheritdoc />
        public async Task<Transaction> Swap(
            SwapQuoteAg quoteResponse,
            PublicKey? userPublicKey = null,
            PublicKey? destinationTokenAccount = null,
            bool wrapAndUnwrapSol = true,
            bool useSharedAccounts = true,
            bool asLegacy = true)
        {
            userPublicKey ??= _account;

            // Construct the request URL
            var apiUrl = _endpoint + "/swap";

            var req = new SwapRequest()
            {
                quoteResponse = quoteResponse,
                userPublicKey = userPublicKey,
                wrapAndUnwrapSol = wrapAndUnwrapSol,
                useSharedAccounts = useSharedAccounts,
                asLegacyTransaction = asLegacy,
            };

            var requestJson = JsonConvert.SerializeObject(req, _serializerOptions);
            var buffer = Encoding.UTF8.GetBytes(requestJson);

            using var httpReq = new HttpRequestMessage(HttpMethod.Post, apiUrl)
            {
                Content = new ByteArrayContent(buffer)
                {
                    Headers = {
                    { "Content-Type", "application/json"}
                }
                }
            };

            // execute POST
            using var response = await _httpClient.SendAsync(httpReq);

            if (response.IsSuccessStatusCode)
            {
                var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var res = JsonConvert.DeserializeObject<SwapResponse>(responseBody, _serializerOptions);
                if (res != null)
                    return Transaction.Deserialize(res.SwapTransaction);
            }

            // Handle error scenarios
            throw new HttpRequestException($"Request failed with status code: {response.StatusCode}");
        }

        private async Task<IList<TokenData>?> GetTokens(string queryString)
        {
            string url = $"https://tokens.jup.ag{queryString}";
            List<TokenData> tokens = new List<TokenData>();

            //ToDo: Some form of caching 

            using var client = new HttpClient();
            using var httpReq = new HttpRequestMessage(HttpMethod.Get, url);
            HttpResponseMessage result = await _httpClient.SendAsync(httpReq);
            string response = await result.Content.ReadAsStringAsync();

            if (queryString.Contains("/token/") && !string.IsNullOrEmpty(response))
            {
                var token = JsonConvert.DeserializeObject<TokenData>(response);

                if (token != null)
                    tokens = new List<TokenData>() { token };
            }
            else
            {
                tokens = JsonConvert.DeserializeObject<List<TokenData>>(response);
            }

            return tokens;
        }

        /// <inheritdoc />
        public async Task<IList<TokenData>?> GetTokens(TokenListType tokenListType = TokenListType.Verified)
        {
            string query = tokenListType == TokenListType.Verified ? "/tokens?tags=verified" : "/tokens?tags=verified,unknown";
            return await GetTokens(query);
        }

        /// <inheritdoc />
        public async Task<TokenData?> GetTokenBySymbol(string symbol)
        {
            IList<TokenData>? tokens = await GetTokens(TokenListType.All);
            if (tokens == null)
                return null;

            return tokens.FirstOrDefault(t =>
                string.Equals(t.Symbol, symbol, StringComparison.CurrentCultureIgnoreCase) ||
                string.Equals(t.Symbol, $"${symbol}", StringComparison.CurrentCultureIgnoreCase));
        }

        /// <inheritdoc />
        public async Task<TokenData?> GetTokenByMint(string mint)
        {
            string query = string.Format("/token/{0}", mint);
            var tokens = await GetTokens(query);
            return tokens.FirstOrDefault();
        }
    }
}