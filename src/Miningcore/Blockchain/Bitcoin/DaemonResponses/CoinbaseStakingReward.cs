using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Miningcore.Blockchain.Bitcoin.DaemonResponses
{
    public class CoinbaseStakingRewardPayoutScript
    {
        [JsonProperty("hex")]
        public string ScriptPubkey { get; set; }
    }

    public class CoinbaseStakingRewardTemplateExtra
    {
        public CoinbaseStakingRewardPayoutScript PayoutScript { get; set; }
        public ulong MinimumValue { get; set; }
    }
}
