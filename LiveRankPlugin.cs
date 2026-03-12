using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace LeagueDeck
{
    [PluginActionId("dev.kubo.leaguedeck.liverank")]
    public class LiveRankPlugin : PluginBase
    {
        private LcuClient _lcu;
        private LiveRankSettings _settings;

        private string _lastRankDisplay;
        private int _tickCounter;

        public LiveRankPlugin(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "LiveRankPlugin - Constructor");

            _lcu = LcuClient.Instance;

            if (payload.Settings == null || !payload.Settings.HasValues)
                _settings = LiveRankSettings.CreateDefaultSettings();
            else
                _settings = payload.Settings.ToObject<LiveRankSettings>();

            Task.Run(async () =>
            {
                using (var icon = Utilities.LoadIcon("ranked.png") ?? Utilities.GenerateIcon("R", Color.FromArgb(180, 130, 30)))
                    await Connection.SetImageAsync(Utilities.ImageToBase64(icon));
                await Connection.SetTitleAsync("Rank");
            });
        }

        public override void KeyPressed(KeyPayload payload)
        {
            // Toggle between Solo/Duo and Flex
            _settings.QueueType = _settings.QueueType == ERankQueueType.SoloDuo
                ? ERankQueueType.Flex
                : ERankQueueType.SoloDuo;
            _lastRankDisplay = null;
            Task.Run(async () =>
            {
                await Connection.SetSettingsAsync(JObject.FromObject(_settings));
                await Connection.SetTitleAsync("...");
            });
        }

        public override void KeyReleased(KeyPayload payload) { }

        public override async void OnTick()
        {
            _tickCounter++;
            if (_tickCounter % 5 != 0) // every ~5 seconds
                return;

            try
            {
                if (!_lcu.IsConnected && !_lcu.TryConnect())
                {
                    if (_lastRankDisplay != null)
                    {
                        _lastRankDisplay = null;
                        await Connection.SetTitleAsync("Rank");
                    }
                    return;
                }

                var stats = await _lcu.GetRankedStats();
                if (stats == null)
                    return;

                var queueKey = _settings.QueueType == ERankQueueType.SoloDuo
                    ? "RANKED_SOLO_5x5"
                    : "RANKED_FLEX_SR";

                var queue = stats.SelectToken($"queueMap.{queueKey}");
                if (queue == null)
                    return;

                var tier = queue["tier"]?.Value<string>() ?? "";
                var division = queue["division"]?.Value<string>() ?? "";
                var lp = queue["leaguePoints"]?.Value<int>() ?? 0;

                string display;
                if (string.IsNullOrEmpty(tier) || tier == "NONE")
                {
                    display = _settings.QueueType == ERankQueueType.SoloDuo ? "Solo\nUnranked" : "Flex\nUnranked";
                }
                else
                {
                    var shortTier = GetShortTier(tier);
                    var queueLabel = _settings.QueueType == ERankQueueType.SoloDuo ? "Solo" : "Flex";
                    display = $"{queueLabel}\n{shortTier} {division}\n{lp}LP";
                }

                if (display != _lastRankDisplay)
                {
                    _lastRankDisplay = display;
                    await Connection.SetTitleAsync(display);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"LiveRankPlugin OnTick error: {ex.Message}");
            }
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            _settings = payload.Settings.ToObject<LiveRankSettings>();
            _lastRankDisplay = null;
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        public override void Dispose() { }

        private string GetShortTier(string tier)
        {
            switch (tier.ToUpper())
            {
                case "IRON": return "Iron";
                case "BRONZE": return "Brnz";
                case "SILVER": return "Slvr";
                case "GOLD": return "Gold";
                case "PLATINUM": return "Plat";
                case "EMERALD": return "Emld";
                case "DIAMOND": return "Dia";
                case "MASTER": return "Mstr";
                case "GRANDMASTER": return "GM";
                case "CHALLENGER": return "Chal";
                default: return tier;
            }
        }
    }

    public enum ERankQueueType
    {
        SoloDuo = 0,
        Flex = 1,
    }

    public class LiveRankSettings
    {
        public static LiveRankSettings CreateDefaultSettings()
        {
            return new LiveRankSettings { QueueType = ERankQueueType.SoloDuo };
        }

        [JsonProperty("queueType")]
        public ERankQueueType QueueType { get; set; }
    }
}
