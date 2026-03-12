using Newtonsoft.Json;

namespace LeagueDeck
{
    public class LiveStatsSettings
    {
        public static LiveStatsSettings CreateDefaultSettings()
        {
            return new LiveStatsSettings
            {
                DisplayFormat = ELiveStatsDisplay.All,
            };
        }

        [JsonProperty("displayFormat")]
        public ELiveStatsDisplay DisplayFormat { get; set; }
    }
}
