using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LeagueDeck
{
    [PluginActionId("dev.kubo.leaguedeck.autosummoners")]
    public class AutoSummonersPlugin : PluginBase
    {
        // Summoner spell name -> ID mapping
        private static readonly Dictionary<string, int> SpellIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Barrier", 21 },
            { "Cleanse", 1 },
            { "Exhaust", 3 },
            { "Flash", 4 },
            { "Ghost", 6 },
            { "Heal", 7 },
            { "Ignite", 14 },
            { "Smite", 11 },
            { "Teleport", 12 },
            { "Mark", 32 },       // ARAM snowball
        };

        private LcuClient _lcu;
        private AutoSummonersSettings _settings;

        private bool _applied;
        private int _tickCounter;

        public AutoSummonersPlugin(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "AutoSummonersPlugin - Constructor");

            _lcu = LcuClient.Instance;

            if (payload.Settings == null || !payload.Settings.HasValues)
                _settings = AutoSummonersSettings.CreateDefaultSettings();
            else
                _settings = payload.Settings.ToObject<AutoSummonersSettings>();

            UpdateTitle();
        }

        public override async void KeyPressed(KeyPayload payload)
        {
            if (!_lcu.IsConnected && !_lcu.TryConnect())
            {
                await Connection.SetTitleAsync("No\nClient");
                return;
            }

            var spell1Id = GetSpellId(_settings.Spell1);
            var spell2Id = GetSpellId(_settings.Spell2);

            if (spell1Id == 0 || spell2Id == 0)
            {
                await Connection.SetTitleAsync("Set\nSpells");
                return;
            }

            var success = await _lcu.SetSummonerSpells(spell1Id, spell2Id);
            await Connection.SetTitleAsync(success ? "Applied!" : "Failed");
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
                if (string.IsNullOrEmpty(_settings.Spell1) || string.IsNullOrEmpty(_settings.Spell2))
                    return;

                if (!_lcu.IsConnected && !_lcu.TryConnect())
                    return;

                var phase = await _lcu.GetGameflowPhase();
                if (phase != "ChampSelect")
                {
                    if (_applied)
                    {
                        _applied = false;
                        UpdateTitle();
                    }
                    return;
                }

                if (_applied)
                    return;

                var champId = await _lcu.GetMyChampionId();
                if (champId <= 0)
                    return;

                var spell1Id = GetSpellId(_settings.Spell1);
                var spell2Id = GetSpellId(_settings.Spell2);

                if (spell1Id == 0 || spell2Id == 0)
                    return;

                var success = await _lcu.SetSummonerSpells(spell1Id, spell2Id);
                if (success)
                {
                    _applied = true;
                    await Connection.SetTitleAsync("Spells\nSet!");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"AutoSummonersPlugin OnTick error: {ex.Message}");
            }
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            _settings = payload.Settings.ToObject<AutoSummonersSettings>();
            _applied = false;
            UpdateTitle();
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        public override void Dispose() { }

        private int GetSpellId(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;
            return SpellIds.TryGetValue(name, out var id) ? id : 0;
        }

        private void UpdateTitle()
        {
            var s1 = string.IsNullOrEmpty(_settings.Spell1) ? "?" : _settings.Spell1;
            var s2 = string.IsNullOrEmpty(_settings.Spell2) ? "?" : _settings.Spell2;
            Task.Run(async () => await Connection.SetTitleAsync($"{s1}\n{s2}"));
        }
    }

    public class AutoSummonersSettings
    {
        public static AutoSummonersSettings CreateDefaultSettings()
        {
            return new AutoSummonersSettings { Spell1 = "Flash", Spell2 = "Teleport" };
        }

        [JsonProperty("spell1")]
        public string Spell1 { get; set; }

        [JsonProperty("spell2")]
        public string Spell2 { get; set; }
    }
}
