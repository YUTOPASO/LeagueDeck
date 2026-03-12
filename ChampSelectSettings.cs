using Newtonsoft.Json;

namespace LeagueDeck
{
    public class ChampSelectSettings
    {
        public static ChampSelectSettings CreateDefaultSettings()
        {
            return new ChampSelectSettings
            {
                Summoner = ESummoner.Summoner1,
            };
        }

        [JsonProperty("summoner")]
        public ESummoner Summoner { get; set; }
    }
}
