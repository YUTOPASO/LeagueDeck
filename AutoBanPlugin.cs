using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LeagueDeck
{
    [PluginActionId("dev.kubo.leaguedeck.autoban")]
    public class AutoBanPlugin : PluginBase
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private LeagueInfo _info;
        private LcuClient _lcu;
        private AutoBanSettings _settings;

        private bool _banned;
        private int _tickCounter;

        public AutoBanPlugin(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "AutoBanPlugin - Constructor");

            _info = LeagueInfo.GetInstance(_cts.Token);
            _lcu = LcuClient.Instance;

            if (payload.Settings == null || !payload.Settings.HasValues)
                _settings = AutoBanSettings.CreateDefaultSettings();
            else
                _settings = payload.Settings.ToObject<AutoBanSettings>();

            UpdateTitle();
        }

        public override async void KeyPressed(KeyPayload payload)
        {
            if (!_lcu.IsConnected && !_lcu.TryConnect())
            {
                await Connection.SetTitleAsync("No\nClient");
                return;
            }

            if (string.IsNullOrEmpty(_settings.ChampionName))
            {
                await Connection.SetTitleAsync("Set\nChamp");
                return;
            }

            var champion = _info.GetChampion(_settings.ChampionName);
            if (champion.Key <= 0)
            {
                await Connection.SetTitleAsync("Unknown\nChamp");
                return;
            }

            var success = await _lcu.BanChampion(champion.Key);
            await Connection.SetTitleAsync(success ? "Banned!" : "Failed");
            await Task.Delay(2000);
            UpdateTitle();
        }

        public override void KeyReleased(KeyPayload payload) { }

        public override async void OnTick()
        {
            _tickCounter++;
            if (_tickCounter % 3 != 0)
                return;

            try
            {
                if (string.IsNullOrEmpty(_settings.ChampionName))
                    return;

                if (!_lcu.IsConnected && !_lcu.TryConnect())
                    return;

                var phase = await _lcu.GetGameflowPhase();
                if (phase != "ChampSelect")
                {
                    if (_banned)
                    {
                        _banned = false;
                        UpdateTitle();
                    }
                    return;
                }

                if (_banned)
                    return;

                var champion = _info.GetChampion(_settings.ChampionName);
                if (champion.Key <= 0)
                    return;

                var success = await _lcu.BanChampion(champion.Key);
                if (success)
                {
                    _banned = true;
                    await Connection.SetTitleAsync($"Banned!\n{champion.Name}");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"AutoBanPlugin OnTick error: {ex.Message}");
            }
        }

        public override async void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            _settings = payload.Settings.ToObject<AutoBanSettings>();
            _banned = false;
            UpdateTitle();
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        public override void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }

        private void UpdateTitle()
        {
            var name = string.IsNullOrEmpty(_settings.ChampionName) ? "---" : _settings.ChampionName;
            Task.Run(async () => await Connection.SetTitleAsync($"Ban\n{name}"));
        }
    }

    public class AutoBanSettings
    {
        public static AutoBanSettings CreateDefaultSettings()
        {
            return new AutoBanSettings { ChampionName = "" };
        }

        [JsonProperty("championName")]
        public string ChampionName { get; set; }
    }
}
