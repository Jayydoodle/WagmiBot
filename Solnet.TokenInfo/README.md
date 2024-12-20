# Solnet.TokenInfo

## Overview

`Solnet.TokenInfo` is a .NET library for retrieving comprehensive information about Solana tokens directly from the blockchain. It provides an easy-to-use service for fetching token metadata, supply, and market data.

## Features

- Fetch token supply from Solana blockchain
- Retrieve market data using Jupiter Aggregator API
- Support for getting token information using contract address
- Comprehensive token information model

## Installation

Install the package via NuGet:

```bash
dotnet add package Solnet.TokenInfo
```

## Usage

### Basic Usage

```csharp
using Solnet.TokenInfo.Services;
using Solnet.TokenInfo.Models;

// Create an instance of the token info service
var tokenInfoService = new SolanaTokenInfoService();

// Fetch token information by contract address
string contractAddress = "YOUR_TOKEN_CONTRACT_ADDRESS";
TokenInfoModel tokenInfo = await tokenInfoService.GetTokenInfoAsync(contractAddress);

// Access token information
Console.WriteLine($"Token: {tokenInfo.Name} ({tokenInfo.Symbol})");
Console.WriteLine($"Price (USD): ${tokenInfo.PriceInUsd}");
Console.WriteLine($"Market Cap: ${tokenInfo.MarketCap}");
Console.WriteLine($"24h Volume: ${tokenInfo.Volume24H}");
Console.WriteLine($"Total Supply: {tokenInfo.TotalSupply}");
```

## Customization

### Custom RPC Endpoint

You can specify a custom Solana RPC endpoint:

```csharp
var tokenInfoService = new SolanaTokenInfoService("https://your-custom-rpc-endpoint.com");
```

## Limitations

- Market data is fetched from Jupiter Aggregator API and may not be available for all tokens
- Metadata retrieval is currently a placeholder and requires further implementation
- Error handling is basic and logs to console

## Dependencies

- Solnet.Rpc
- Solnet.Wallet
- Newtonsoft.Json

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

[Specify your license here]
