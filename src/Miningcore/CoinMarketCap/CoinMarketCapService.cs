using Newtonsoft.Json;
using Miningcore.Api.Requests;

namespace Miningcore.CoinMarketCap
{
    public class CoinMarketCapService
    {
        private readonly IHttpClientFactory _factory;

        public CoinMarketCapService(IHttpClientFactory factory)
        {
            _factory = factory;
        }

        public async Task<CryptoApiResponse> GetCryptoQuoteAsync(string symbol)
        {
            using var client = _factory.CreateClient(CoinMarketCapConstants.HttpKeyword);
            var response = await client.GetAsync($"cryptocurrency/quotes/latest?symbol={symbol}");
            var result = await response.Content.ReadAsStringAsync();
            var resultValue = JsonConvert.DeserializeObject<CryptoApiResponse>(result);

            var slugs = string.Join(",", resultValue.Data.Select(Q => string.Join(",", Q.Value.Select(Q => Q.Slug)))).ToLower().Replace(",,", ",");
            slugs = slugs.Substring(slugs.Length -1 , 1) == "," ? slugs.Remove(slugs.Length - 1) : slugs;
            response = await client.GetAsync($"cryptocurrency/info?slug={slugs}");
            var resultLogo = await response.Content.ReadAsStringAsync();
            var resultValueInfo = JsonConvert.DeserializeObject<InfoApiResponse>(resultLogo);
            var allDictionary = resultValueInfo?.Data?.GroupBy(Q => Q.Value.Symbol)
                                                        .ToDictionary(
                                                            g => g.Key,
                                                            g => g.ToDictionary(
                                                                x => x.Key.ToString(),
                                                                x => x.Value.Logo
                                                            )
                                                        ) ?? new Dictionary<string, Dictionary<string, string>>();
            foreach(var item in resultValue.Data)
            {
                if(allDictionary.Count == 0)
                    break;
                if(allDictionary.TryGetValue(item.Key, out Dictionary<string, string> logoDictionary))
                {
                    foreach(var cryptoData in item.Value)
                    {
                        if(logoDictionary.TryGetValue(cryptoData.Id.ToString(), out string logo))
                        {
                            cryptoData.Logo = logo;
                        }
                    }
                }
            }
            return resultValue;
        }
    }
}

