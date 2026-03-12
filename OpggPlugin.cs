using BarRaider.SdTools;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace LeagueDeck
{
    [PluginActionId("dev.kubo.leaguedeck.opgg")]
    public class OpggPlugin : PluginBase
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private LeagueInfo _info;
        private LcuClient _lcu;

        private int _lastChampionId;
        private int _tickCounter;

        public OpggPlugin(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "OpggPlugin - Constructor");

            LeagueInfo.OnUpdateStarted += LeagueInfo_OnUpdateStarted;
            LeagueInfo.OnUpdateProgress += LeagueInfo_OnUpdateProgress;
            LeagueInfo.OnUpdateCompleted += LeagueInfo_OnUpdateCompleted;

            _info = LeagueInfo.GetInstance(_cts.Token);
            _lcu = LcuClient.Instance;

            if (_info.UpdateTask != null)
            {
                Task.Run(async () =>
                {
                    var image = Utilities.GetUpdateImage();
                    await Connection.SetImageAsync(image);
                    await _info.UpdateTask;
                    await Connection.SetDefaultImageAsync();
                    await Connection.SetTitleAsync("OP.GG");
                });
            }
            else
            {
                Task.Run(async () => await Connection.SetTitleAsync("OP.GG"));
            }
        }

        #region Events

        private async void LeagueInfo_OnUpdateStarted(object sender, LeagueInfo.UpdateEventArgs e)
        {
            await Connection.SetImageAsync(Utilities.GetUpdateImage());
        }

        private async void LeagueInfo_OnUpdateProgress(object sender, LeagueInfo.UpdateEventArgs e)
        {
            await Connection.SetTitleAsync($"{e.Progress}%");
        }

        private async void LeagueInfo_OnUpdateCompleted(object sender, LeagueInfo.UpdateEventArgs e)
        {
            await Connection.SetDefaultImageAsync();
            await Connection.SetTitleAsync("OP.GG");
        }

        #endregion

        #region Overrides

        public override async void KeyPressed(KeyPayload payload)
        {
            try
            {
                string championName = null;

                // Try to get champion from champ select via LCU
                if (_lcu.IsConnected || _lcu.TryConnect())
                {
                    var champId = await _lcu.GetMyChampionId();
                    if (champId > 0)
                    {
                        var champion = _info.GetChampionByKey(champId);
                        if (champion.Key > 0)
                            championName = champion.Id;
                    }
                }

                // Try to get champion from in-game API
                if (championName == null)
                {
                    var activePlayer = await _info.GetActivePlayer(_cts.Token);
                    if (activePlayer != null)
                    {
                        var summoner = await _info.GetSummoner(activePlayer.Name, _cts.Token);
                        if (summoner != null)
                        {
                            var champion = _info.GetChampion(summoner.ChampionName);
                            if (champion.Id != "???")
                                championName = champion.Id;
                        }
                    }
                }

                string url;
                if (championName != null)
                    url = $"https://www.op.gg/champions/{championName.ToLower()}/build?region=jp";
                else
                    url = "https://www.op.gg/?region=jp";

                Process.Start(url);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"OpggPlugin KeyPressed error: {ex.Message}");
                Process.Start("https://www.op.gg/?region=jp");
            }
        }

        public override void KeyReleased(KeyPayload payload) { }

        public override async void OnTick()
        {
            _tickCounter++;
            if (_tickCounter % 3 != 0)
                return;

            try
            {
                int champId = 0;

                // Try LCU for champ select
                if (_lcu.IsConnected || _lcu.TryConnect())
                {
                    var phase = await _lcu.GetGameflowPhase();
                    if (phase == "ChampSelect")
                        champId = await _lcu.GetMyChampionId();
                }

                if (champId > 0 && champId != _lastChampionId)
                {
                    _lastChampionId = champId;
                    var champion = _info.GetChampionByKey(champId);
                    var champImage = _info.GetChampionImage(champion.Id);
                    await Connection.SetImageAsync(champImage);
                    await Connection.SetTitleAsync($"OP.GG\n{champion.Name}");
                }
                else if (champId == 0 && _lastChampionId != 0)
                {
                    _lastChampionId = 0;
                    await Connection.SetDefaultImageAsync();
                    await Connection.SetTitleAsync("OP.GG");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"OpggPlugin OnTick error: {ex.Message}");
            }
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload) { }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        public override void Dispose()
        {
            LeagueInfo.OnUpdateStarted -= LeagueInfo_OnUpdateStarted;
            LeagueInfo.OnUpdateProgress -= LeagueInfo_OnUpdateProgress;
            LeagueInfo.OnUpdateCompleted -= LeagueInfo_OnUpdateCompleted;

            _cts.Cancel();
            _cts.Dispose();
        }

        #endregion
    }
}
