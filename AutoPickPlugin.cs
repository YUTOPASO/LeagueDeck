using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace LeagueDeck
{
    [PluginActionId("dev.kubo.leaguedeck.autopick")]
    public class AutoPickPlugin : PluginBase
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private LeagueInfo _info;
        private LcuClient _lcu;
        private AutoPickSettings _settings;

        private bool _picked;
        private int _tickCounter;

        public AutoPickPlugin(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "AutoPickPlugin - Constructor");

            _info = LeagueInfo.GetInstance(_cts.Token);
            _lcu = LcuClient.Instance;

            if (payload.Settings == null || !payload.Settings.HasValues)
                _settings = AutoPickSettings.CreateDefaultSettings();
            else
                _settings = payload.Settings.ToObject<AutoPickSettings>();

            Task.Run(async () =>
            {
                using (var icon = Utilities.GenerateIcon("AP", Color.FromArgb(30, 120, 200)))
                    await Connection.SetImageAsync(Utilities.ImageToBase64(icon));
            });
            UpdateTitle();
        }

        public override async void KeyPressed(KeyPayload payload)
        {
            // Manual trigger: try to pick now
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

            var success = await _lcu.PickChampion(champion.Key);
            await Connection.SetTitleAsync(success ? "Picked!" : "Failed");
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
                    if (_picked)
                    {
                        _picked = false;
                        UpdateTitle();
                    }
                    return;
                }

                if (_picked)
                    return;

                var champion = _info.GetChampion(_settings.ChampionName);
                if (champion.Key <= 0)
                    return;

                var success = await _lcu.PickChampion(champion.Key);
                if (success)
                {
                    _picked = true;
                    await Connection.SetTitleAsync($"Picked!\n{champion.Name}");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"AutoPickPlugin OnTick error: {ex.Message}");
            }
        }

        public override async void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            _settings = payload.Settings.ToObject<AutoPickSettings>();
            _picked = false;
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
            Task.Run(async () => await Connection.SetTitleAsync($"Pick\n{name}"));
        }
    }

    public class AutoPickSettings
    {
        public static AutoPickSettings CreateDefaultSettings()
        {
            return new AutoPickSettings { ChampionName = "" };
        }

        [JsonProperty("championName")]
        public string ChampionName { get; set; }
    }
}
