using BarRaider.SdTools;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LeagueDeck
{
    [PluginActionId("dev.kubo.leaguedeck.champselect")]
    public class ChampSelectPlugin : PluginBase
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private ChampSelectSettings _settings;
        private LeagueInfo _info;
        private LcuClient _lcu;

        private bool _wasInChampSelect;
        private int _tickCounter;

        public ChampSelectPlugin(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "ChampSelectPlugin - Constructor");

            LeagueInfo.OnUpdateStarted += LeagueInfo_OnUpdateStarted;
            LeagueInfo.OnUpdateProgress += LeagueInfo_OnUpdateProgress;
            LeagueInfo.OnUpdateCompleted += LeagueInfo_OnUpdateCompleted;

            _info = LeagueInfo.GetInstance(_cts.Token);
            _lcu = LcuClient.Instance;

            if (payload.Settings == null || payload.Settings.Count == 0)
                this._settings = ChampSelectSettings.CreateDefaultSettings();
            else
                this._settings = payload.Settings.ToObject<ChampSelectSettings>();

            if (_info.UpdateTask != null)
            {
                Task.Run(async () =>
                {
                    var image = Utilities.GetUpdateImage();
                    await Connection.SetImageAsync(image);

                    await _info.UpdateTask;

                    await Connection.SetDefaultImageAsync();
                    await Connection.SetTitleAsync(string.Empty);
                });
            }
        }

        #region Events

        private async void LeagueInfo_OnUpdateStarted(object sender, LeagueInfo.UpdateEventArgs e)
        {
            var image = Utilities.GetUpdateImage();
            await Connection.SetImageAsync(image);
        }

        private async void LeagueInfo_OnUpdateProgress(object sender, LeagueInfo.UpdateEventArgs e)
        {
            await Connection.SetTitleAsync($"{e.Progress}%");
        }

        private async void LeagueInfo_OnUpdateCompleted(object sender, LeagueInfo.UpdateEventArgs e)
        {
            await Connection.SetDefaultImageAsync();
            await Connection.SetTitleAsync(string.Empty);
        }

        #endregion

        #region Overrides

        public override void KeyPressed(KeyPayload payload) { }

        public override void KeyReleased(KeyPayload payload) { }

        public override async void OnTick()
        {
            // Only check every 2 ticks (~2 seconds) to reduce API load
            _tickCounter++;
            if (_tickCounter % 2 != 0)
                return;

            try
            {
                // Try to connect to LCU if not connected
                if (!_lcu.IsConnected)
                {
                    var processes = Process.GetProcessesByName("LeagueClientUx");
                    if (processes.Length == 0)
                    {
                        if (_wasInChampSelect)
                        {
                            _wasInChampSelect = false;
                            await Connection.SetTitleAsync(string.Empty);
                        }
                        return;
                    }

                    if (!_lcu.TryConnect())
                        return;
                }

                var phase = await _lcu.GetGameflowPhase();

                if (phase == null)
                {
                    _lcu.Disconnect();
                    if (_wasInChampSelect)
                    {
                        _wasInChampSelect = false;
                        await Connection.SetTitleAsync(string.Empty);
                    }
                    return;
                }

                if (phase == "ChampSelect")
                {
                    _wasInChampSelect = true;
                    await UpdateChampSelectDisplay();
                }
                else if (_wasInChampSelect)
                {
                    _wasInChampSelect = false;
                    await Connection.SetTitleAsync(string.Empty);
                }
                else if (phase == "Lobby" || phase == "Matchmaking" || phase == "ReadyCheck")
                {
                    await Connection.SetTitleAsync("Lobby");
                }
                else if (phase == "InProgress")
                {
                    await Connection.SetTitleAsync("In Game");
                }
                else
                {
                    await Connection.SetTitleAsync(string.Empty);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"ChampSelectPlugin OnTick error: {ex.Message}");
            }
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Tools.AutoPopulateSettings(_settings, payload.Settings);
            Connection.SetSettingsAsync(JObject.FromObject(_settings));
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        public override void Dispose()
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "ChampSelectPlugin - Dispose");

            LeagueInfo.OnUpdateStarted -= LeagueInfo_OnUpdateStarted;
            LeagueInfo.OnUpdateProgress -= LeagueInfo_OnUpdateProgress;
            LeagueInfo.OnUpdateCompleted -= LeagueInfo_OnUpdateCompleted;

            _cts.Cancel();
            _cts.Dispose();
        }

        #endregion

        #region Private Methods

        private async Task UpdateChampSelectDisplay()
        {
            var session = await _lcu.GetChampSelectSession();
            if (session == null)
                return;

            var theirTeam = session["theirTeam"] as JArray;
            if (theirTeam == null || theirTeam.Count == 0)
            {
                await Connection.SetTitleAsync("Pick\nPhase");
                return;
            }

            var enemyNames = new List<string>();
            foreach (var member in theirTeam)
            {
                var championId = member["championId"]?.Value<int>() ?? 0;
                if (championId > 0)
                {
                    var champion = _info.GetChampionByKey(championId);
                    enemyNames.Add(champion.Name);
                }
            }

            if (enemyNames.Count == 0)
            {
                await Connection.SetTitleAsync("Pick\nPhase");
                return;
            }

            var title = "VS\n" + string.Join("\n", enemyNames.Take(3));
            if (enemyNames.Count > 3)
                title += $"\n+{enemyNames.Count - 3}";

            await Connection.SetTitleAsync(title);
        }

        #endregion
    }
}
