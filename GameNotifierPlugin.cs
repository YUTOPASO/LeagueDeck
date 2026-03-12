using BarRaider.SdTools;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace LeagueDeck
{
    [PluginActionId("dev.kubo.leaguedeck.gamenotifier")]
    public class GameNotifierPlugin : PluginBase
    {
        private CancellationTokenSource _cts = new CancellationTokenSource();

        private LeagueInfo _info;

        private bool _isInGame;
        private bool _gameEnded;

        public GameNotifierPlugin(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "GameNotifierPlugin - Constructor");

            _info = LeagueInfo.GetInstance(_cts.Token);

            Connection.OnApplicationDidLaunch += Connection_OnApplicationDidLaunch;
            Connection.OnApplicationDidTerminate += Connection_OnApplicationDidTerminate;

            Task.Run(async () =>
            {
                using (var icon = Utilities.GenerateIcon("GN", Color.FromArgb(50, 150, 80)))
                    await Connection.SetImageAsync(Utilities.ImageToBase64(icon));
                await Connection.SetTitleAsync(string.Empty);
            });
        }

        #region Events

        private async void Connection_OnApplicationDidLaunch(object sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.ApplicationDidLaunch> e)
        {
            if (e.Event.Payload.Application != "League of Legends.exe")
                return;

            Logger.Instance.LogMessage(TracingLevel.DEBUG, "GameNotifierPlugin - Game launched");
            _isInGame = true;
            _gameEnded = false;
            await Connection.SetTitleAsync("Loading");
        }

        private async void Connection_OnApplicationDidTerminate(object sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.ApplicationDidTerminate> e)
        {
            if (e.Event.Payload.Application != "League of Legends.exe")
                return;

            Logger.Instance.LogMessage(TracingLevel.DEBUG, "GameNotifierPlugin - Game terminated");
            _isInGame = false;
            _gameEnded = true;
            await Connection.SetTitleAsync("GG");
        }

        #endregion

        #region Overrides

        public override void KeyPressed(KeyPayload payload)
        {
            if (_gameEnded)
            {
                _gameEnded = false;
                Task.Run(async () => await Connection.SetTitleAsync(string.Empty));
            }
        }

        public override void KeyReleased(KeyPayload payload) { }

        public override async void OnTick()
        {
            if (!_isInGame)
                return;

            try
            {
                var gameData = await _info.GetGameData(_cts.Token);
                if (gameData == null)
                {
                    await Connection.SetTitleAsync("Loading");
                    return;
                }

                var totalSeconds = (int)gameData.Time;
                var minutes = totalSeconds / 60;
                var seconds = totalSeconds % 60;
                await Connection.SetTitleAsync($"{minutes}:{seconds:D2}");
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"GameNotifierPlugin OnTick error: {ex.Message}");
            }
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload) { }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        public override void Dispose()
        {
            Connection.OnApplicationDidLaunch -= Connection_OnApplicationDidLaunch;
            Connection.OnApplicationDidTerminate -= Connection_OnApplicationDidTerminate;

            _cts.Cancel();
            _cts.Dispose();
        }

        #endregion
    }
}
