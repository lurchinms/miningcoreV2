using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Miningcore.Api.Requests
{
    public class CryptoApiResponse
    {
        [JsonPropertyName("status")]
        [JsonProperty("status")]
        public StatusInfo Status { get; set; }

        [JsonPropertyName("data")]
        [JsonProperty("data")]
        public Dictionary<string, List<CoinMarketCapData>> Data { get; set; }
    }

    public class StatusInfo
    {
        [JsonPropertyName("timestamp")]
        [JsonProperty("timestamp")]
        public DateTime? Timestamp { get; set; }

        [JsonPropertyName("error_code")]
        [JsonProperty("error_code")]
        public int ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }

        [JsonPropertyName("elapsed")]
        [JsonProperty("elapsed")]
        public int Elapsed { get; set; }

        [JsonPropertyName("credit_count")]
        [JsonProperty("credit_count")]
        public int CreditCount { get; set; }

        [JsonPropertyName("notice")]
        [JsonProperty("notice")]
        public string Notice { get; set; }
    }

    public class CoinMarketCapData
    {
        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonPropertyName("symbol")]
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonPropertyName("logo")]
        [JsonProperty("logo")]
        public string Logo { get; set; } = "n/a";

        [JsonPropertyName("slug")]
        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonPropertyName("num_market_pairs")]
        [JsonProperty("num_market_pairs")]
        public int? NumMarketPairs { get; set; }

        [JsonPropertyName("date_added")]
        [JsonProperty("date_added")]
        public DateTime? DateAdded { get; set; }

        [JsonPropertyName("tags")]
        [JsonProperty("tags")]
        public List<Tag> Tags { get; set; }

        [JsonPropertyName("max_supply")]
        [JsonProperty("max_supply")]
        public long? MaxSupply { get; set; }

        [JsonPropertyName("circulating_supply")]
        [JsonProperty("circulating_supply")]
        public double CirculatingSupply { get; set; }

        [JsonPropertyName("total_supply")]
        [JsonProperty("total_supply")]
        public double TotalSupply { get; set; }

        [JsonPropertyName("is_active")]
        [JsonProperty("is_active")]
        public int IsActive { get; set; }

        [JsonPropertyName("infinite_supply")]
        [JsonProperty("infinite_supply")]
        public bool InfiniteSupply { get; set; }

        [JsonPropertyName("platform")]
        [JsonProperty("platform")]
        public Platform Platform { get; set; }

        [JsonPropertyName("cmc_rank")]
        [JsonProperty("cmc_rank")]
        public int? CmcRank { get; set; }

        [JsonPropertyName("is_fiat")]
        [JsonProperty("is_fiat")]
        public int IsFiat { get; set; }

        [JsonPropertyName("self_reported_circulating_supply")]
        [JsonProperty("self_reported_circulating_supply")]
        public double? SelfReportedCirculatingSupply { get; set; }

        [JsonPropertyName("self_reported_market_cap")]
        [JsonProperty("self_reported_market_cap")]
        public double? SelfReportedMarketCap { get; set; }

        [JsonPropertyName("tvl_ratio")]
        [JsonProperty("tvl_ratio")]
        public double? TvlRatio { get; set; }

        [JsonPropertyName("last_updated")]
        [JsonProperty("last_updated")]
        public DateTime? LastUpdated { get; set; }

        [JsonPropertyName("quote")]
        [JsonProperty("quote")]
        public Dictionary<string, Quote> Quote { get; set; }
    }

    public class Tag
    {
        [JsonPropertyName("slug")]
        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonPropertyName("name")]
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonPropertyName("category")]
        [JsonProperty("category")]
        public string Category { get; set; }
    }

    public class Platform
    {
        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonPropertyName("symbol")]
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonPropertyName("slug")]
        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonPropertyName("token_address")]
        [JsonProperty("token_address")]
        public string TokenAddress { get; set; }
    }

    public class Quote
    {
        [JsonPropertyName("price")]
        [JsonProperty("price")]
        public double? Price { get; set; }

        [JsonPropertyName("volume_24h")]
        [JsonProperty("volume_24h")]
        public double? Volume24H { get; set; }

        [JsonPropertyName("volume_change_24h")]
        [JsonProperty("volume_change_24h")]
        public double? VolumeChange24H { get; set; }

        [JsonPropertyName("percent_change_1h")]
        [JsonProperty("percent_change_1h")]
        public double? PercentChange1H { get; set; }

        [JsonPropertyName("percent_change_24h")]
        [JsonProperty("percent_change_24h")]
        public double? PercentChange24H { get; set; }

        [JsonPropertyName("percent_change_7d")]
        [JsonProperty("percent_change_7d")]
        public double? PercentChange7D { get; set; }

        [JsonPropertyName("percent_change_30d")]
        [JsonProperty("percent_change_30d")]
        public double? PercentChange30D { get; set; }

        [JsonPropertyName("percent_change_60d")]
        [JsonProperty("percent_change_60d")]
        public double? PercentChange60D { get; set; }

        [JsonPropertyName("percent_change_90d")]
        [JsonProperty("percent_change_90d")]
        public double? PercentChange90D { get; set; }

        [JsonPropertyName("market_cap")]
        [JsonProperty("market_cap")]
        public double? MarketCap { get; set; }

        [JsonPropertyName("market_cap_dominance")]
        [JsonProperty("market_cap_dominance")]
        public double? MarketCapDominance { get; set; }

        [JsonPropertyName("fully_diluted_market_cap")]
        [JsonProperty("fully_diluted_market_cap")]
        public double? FullyDilutedMarketCap { get; set; }

        [JsonPropertyName("tvl")]
        [JsonProperty("tvl")]
        public double? Tvl { get; set; }

        [JsonPropertyName("last_updated")]
        [JsonProperty("last_updated")]
        public DateTime? LastUpdated { get; set; }
    }


    public class InfoApiResponse
    {
        [JsonPropertyName("status")]
        [JsonProperty("status")]
        public InfoStatus Status { get; set; }

        [JsonPropertyName("data")]
        [JsonProperty("data")]
        public Dictionary<string, InfoCryptoData> Data { get; set; }
    }

    public class InfoStatus
    {
        [JsonPropertyName("timestamp")]
        [JsonProperty("timestamp")]
        public DateTime? Timestamp { get; set; }

        [JsonPropertyName("error_code")]
        [JsonProperty("error_code")]
        public int ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        [JsonProperty("error_message")]
        public string ErrorMessage { get; set; }

        [JsonPropertyName("elapsed")]
        [JsonProperty("elapsed")]
        public int Elapsed { get; set; }

        [JsonPropertyName("credit_count")]
        [JsonProperty("credit_count")]
        public int CreditCount { get; set; }

        [JsonPropertyName("notice")]
        [JsonProperty("notice")]
        public string Notice { get; set; }
    }

    public class InfoCryptoData
    {
        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonPropertyName("symbol")]
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonPropertyName("category")]
        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonPropertyName("description")]
        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonPropertyName("slug")]
        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonPropertyName("logo")]
        [JsonProperty("logo")]
        public string Logo { get; set; }

        [JsonPropertyName("subreddit")]
        [JsonProperty("subreddit")]
        public string Subreddit { get; set; }

        [JsonPropertyName("notice")]
        [JsonProperty("notice")]
        public string Notice { get; set; }

        [JsonPropertyName("tags")]
        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        [JsonPropertyName("tag-names")]
        [JsonProperty("tag-names")]
        public List<string> TagNames { get; set; }

        [JsonPropertyName("tag-groups")]
        [JsonProperty("tag-groups")]
        public List<string> TagGroups { get; set; }

        [JsonPropertyName("urls")]
        [JsonProperty("urls")]
        public InfoUrls Urls { get; set; }

        [JsonPropertyName("platform")]
        [JsonProperty("platform")]
        public InfoPlatform Platform { get; set; }

        [JsonPropertyName("date_added")]
        [JsonProperty("date_added")]
        public DateTime? DateAdded { get; set; }

        [JsonPropertyName("twitter_username")]
        [JsonProperty("twitter_username")]
        public string TwitterUsername { get; set; }

        [JsonPropertyName("is_hidden")]
        [JsonProperty("is_hidden")]
        public int IsHidden { get; set; }

        [JsonPropertyName("date_launched")]
        [JsonProperty("date_launched")]
        public DateTime? DateLaunched { get; set; }

        [JsonPropertyName("contract_address")]
        [JsonProperty("contract_address")]
        public List<InfoContractAddress> ContractAddress { get; set; }

        [JsonPropertyName("self_reported_circulating_supply")]
        [JsonProperty("self_reported_circulating_supply")]
        public decimal? SelfReportedCirculatingSupply { get; set; }

        [JsonPropertyName("self_reported_tags")]
        [JsonProperty("self_reported_tags")]
        public List<string> SelfReportedTags { get; set; }

        [JsonPropertyName("self_reported_market_cap")]
        [JsonProperty("self_reported_market_cap")]
        public decimal? SelfReportedMarketCap { get; set; }

        [JsonPropertyName("infinite_supply")]
        [JsonProperty("infinite_supply")]
        public bool InfiniteSupply { get; set; }
    }

    public class InfoUrls
    {
        [JsonPropertyName("website")]
        [JsonProperty("website")]
        public List<string> Website { get; set; }

        [JsonPropertyName("twitter")]
        [JsonProperty("twitter")]
        public List<string> Twitter { get; set; }

        [JsonPropertyName("message_board")]
        [JsonProperty("message_board")]
        public List<string> MessageBoard { get; set; }

        [JsonPropertyName("chat")]
        [JsonProperty("chat")]
        public List<string> Chat { get; set; }

        [JsonPropertyName("facebook")]
        [JsonProperty("facebook")]
        public List<string> Facebook { get; set; }

        [JsonPropertyName("explorer")]
        [JsonProperty("explorer")]
        public List<string> Explorer { get; set; }

        [JsonPropertyName("reddit")]
        [JsonProperty("reddit")]
        public List<string> Reddit { get; set; }

        [JsonPropertyName("technical_doc")]
        [JsonProperty("technical_doc")]
        public List<string> TechnicalDoc { get; set; }

        [JsonPropertyName("source_code")]
        [JsonProperty("source_code")]
        public List<string> SourceCode { get; set; }

        [JsonPropertyName("announcement")]
        [JsonProperty("announcement")]
        public List<string> Announcement { get; set; }
    }

    public class InfoPlatform
    {
        [JsonPropertyName("id")]
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonPropertyName("slug")]
        [JsonProperty("slug")]
        public string Slug { get; set; }

        [JsonPropertyName("symbol")]
        [JsonProperty("symbol")]
        public string Symbol { get; set; }

        [JsonPropertyName("token_address")]
        [JsonProperty("token_address")]
        public string TokenAddress { get; set; }
    }

    public class InfoContractAddress
    {
        [JsonPropertyName("contract_address")]
        [JsonProperty("contract_address")]
        public string Address { get; set; }

        [JsonPropertyName("platform")]
        [JsonProperty("platform")]
        public InfoPlatform Platform { get; set; }
    }
}
