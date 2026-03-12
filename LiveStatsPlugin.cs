using BarRaider.SdTools;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LeagueDeck
{
    [PluginActionId("dev.kubo.leaguedeck.livestats")]
    public class LiveStatsPlugin : PluginBase
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private LiveStatsSettings _settings;

        private LeagueInfo _info;
        private bool _isInGame;

        public LiveStatsPlugin(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "LiveStatsPlugin - Constructor");

            LeagueInfo.OnUpdateStarted += LeagueInfo_OnUpdateStarted;
            LeagueInfo.OnUpdateProgress += LeagueInfo_OnUpdateProgress;
            LeagueInfo.OnUpdateCompleted += LeagueInfo_OnUpdateCompleted;

            _info = LeagueInfo.GetInstance(_cts.Token);

            Connection.OnApplicationDidLaunch += Connection_OnApplicationDidLaunch;
            Connection.OnApplicationDidTerminate += Connection_OnApplicationDidTerminate;

            if (payload.Settings == null || payload.Settings.Count == 0)
                this._settings = LiveStatsSettings.CreateDefaultSettings();
            else
                this._settings = payload.Settings.ToObject<LiveStatsSettings>();

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

        private async void Connection_OnApplicationDidLaunch(object sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.ApplicationDidLaunch> e)
        {
            if (e.Event.Payload.Application != "League of Legends.exe")
                return;

            Logger.Instance.LogMessage(TracingLevel.DEBUG, "LiveStatsPlugin - Game started");
            _isInGame = true;
        }

        private async void Connection_OnApplicationDidTerminate(object sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.ApplicationDidTerminate> e)
        {
            if (e.Event.Payload.Application != "League of Legends.exe")
                return;

            _isInGame = false;

            _cts.Cancel();
            _cts = new CancellationTokenSource();

            await Connection.SetDefaultImageAsync();
            await Connection.SetTitleAsync(string.Empty);
        }

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
            if (!_isInGame)
                return;

            try
            {
                var activePlayer = await _info.GetActivePlayer(_cts.Token);
                if (activePlayer == null)
                    return;

                var summoner = await _info.GetSummoner(activePlayer.Name, _cts.Token);
                if (summoner == null || summoner.Scores == null)
                    return;

                var scores = summoner.Scores;
                var gold = activePlayer.CurrentGold;

                string title;
                switch (_settings.DisplayFormat)
                {
                    case ELiveStatsDisplay.KDA:
                        title = $"{scores.Kills}/{scores.Deaths}/{scores.Assists}";
                        break;

                    case ELiveStatsDisplay.CS:
                        title = $"{scores.CreepScore} CS";
                        break;

                    case ELiveStatsDisplay.Gold:
                        title = FormatGold(gold);
                        break;

                    case ELiveStatsDisplay.All:
                        title = $"{scores.Kills}/{scores.Deaths}/{scores.Assists}\n{scores.CreepScore} CS\n{FormatGold(gold)}";
                        break;

                    default:
                        title = string.Empty;
                        break;
                }

                await Connection.SetTitleAsync(title);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"LiveStatsPlugin OnTick error: {ex.Message}");
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
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "LiveStatsPlugin - Dispose");

            LeagueInfo.OnUpdateStarted -= LeagueInfo_OnUpdateStarted;
            LeagueInfo.OnUpdateProgress -= LeagueInfo_OnUpdateProgress;
            LeagueInfo.OnUpdateCompleted -= LeagueInfo_OnUpdateCompleted;

            Connection.OnApplicationDidLaunch -= Connection_OnApplicationDidLaunch;
            Connection.OnApplicationDidTerminate -= Connection_OnApplicationDidTerminate;

            _cts.Cancel();
            _cts.Dispose();
        }

        #endregion

        #region Private Methods

        private string FormatGold(double gold)
        {
            if (gold >= 1000)
                return $"{gold / 1000:F1}k G";
            return $"{(int)gold} G";
        }

        #endregion
    }
}
