using BarRaider.SdTools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LeagueDeck
{
    [PluginActionId("dev.kubo.leaguedeck.objectivetimer")]
    public class ObjectiveTimerPlugin : PluginBase
    {
        private const int DragonRespawnSeconds = 300;  // 5 min
        private const int BaronRespawnSeconds = 360;   // 6 min
        private const int HeraldRespawnSeconds = 360;  // 6 min
        private const int BlinkThreshold = 60;         // blink at 60s remaining

        private CancellationTokenSource _cts = new CancellationTokenSource();

        private LeagueInfo _info;

        private bool _isInGame;
        private int _lastProcessedEventId = -1;

        // Active timers: objective type -> respawn game time (seconds)
        private double _dragonRespawnAt = -1;
        private double _baronRespawnAt = -1;
        private double _heraldRespawnAt = -1;

        private double _currentGameTime;
        private int _tickCounter;
        private bool _blinkOn;

        public ObjectiveTimerPlugin(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "ObjectiveTimerPlugin - Constructor");

            _info = LeagueInfo.GetInstance(_cts.Token);

            Connection.OnApplicationDidLaunch += Connection_OnApplicationDidLaunch;
            Connection.OnApplicationDidTerminate += Connection_OnApplicationDidTerminate;

            Task.Run(async () => await Connection.SetTitleAsync(string.Empty));
        }

        #region Events

        private void Connection_OnApplicationDidLaunch(object sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.ApplicationDidLaunch> e)
        {
            if (e.Event.Payload.Application != "League of Legends.exe")
                return;

            _isInGame = true;
            _lastProcessedEventId = -1;
            _dragonRespawnAt = -1;
            _baronRespawnAt = -1;
            _heraldRespawnAt = -1;
        }

        private void Connection_OnApplicationDidTerminate(object sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.ApplicationDidTerminate> e)
        {
            if (e.Event.Payload.Application != "League of Legends.exe")
                return;

            _isInGame = false;
            Task.Run(async () => await Connection.SetTitleAsync(string.Empty));
        }

        #endregion

        #region Overrides

        public override void KeyPressed(KeyPayload payload) { }
        public override void KeyReleased(KeyPayload payload) { }

        public override async void OnTick()
        {
            if (!_isInGame)
                return;

            _tickCounter++;
            _blinkOn = (_tickCounter % 2 == 0);

            try
            {
                var gameData = await _info.GetGameData(_cts.Token);
                if (gameData == null)
                    return;

                _currentGameTime = gameData.Time;

                // Check for new objective kill events
                var events = await _info.GetEventData(_cts.Token);
                if (events != null)
                {
                    foreach (var evt in events)
                    {
                        if (evt.Id <= _lastProcessedEventId)
                            continue;

                        _lastProcessedEventId = evt.Id;

                        switch (evt.Type)
                        {
                            case EEventType.DragonKill:
                                _dragonRespawnAt = evt.Time + DragonRespawnSeconds;
                                break;
                            case EEventType.BaronKill:
                                _baronRespawnAt = evt.Time + BaronRespawnSeconds;
                                break;
                            case EEventType.HeraldKill:
                                _heraldRespawnAt = evt.Time + HeraldRespawnSeconds;
                                break;
                        }
                    }
                }

                // Clear expired timers
                if (_dragonRespawnAt > 0 && _currentGameTime >= _dragonRespawnAt)
                    _dragonRespawnAt = -1;
                if (_baronRespawnAt > 0 && _currentGameTime >= _baronRespawnAt)
                    _baronRespawnAt = -1;
                if (_heraldRespawnAt > 0 && _currentGameTime >= _heraldRespawnAt)
                    _heraldRespawnAt = -1;

                // Build display text
                var lines = new List<string>();

                if (_dragonRespawnAt > 0)
                {
                    var remaining = (int)(_dragonRespawnAt - _currentGameTime);
                    if (remaining > 0)
                    {
                        var label = FormatTimer("D", remaining);
                        if (remaining <= BlinkThreshold && !_blinkOn)
                            label = "";
                        lines.Add(label);
                    }
                }

                if (_baronRespawnAt > 0)
                {
                    var remaining = (int)(_baronRespawnAt - _currentGameTime);
                    if (remaining > 0)
                    {
                        var label = FormatTimer("B", remaining);
                        if (remaining <= BlinkThreshold && !_blinkOn)
                            label = "";
                        lines.Add(label);
                    }
                }

                if (_heraldRespawnAt > 0)
                {
                    var remaining = (int)(_heraldRespawnAt - _currentGameTime);
                    if (remaining > 0)
                    {
                        var label = FormatTimer("H", remaining);
                        if (remaining <= BlinkThreshold && !_blinkOn)
                            label = "";
                        lines.Add(label);
                    }
                }

                if (lines.Count > 0)
                    await Connection.SetTitleAsync(string.Join("\n", lines));
                else
                    await Connection.SetTitleAsync(string.Empty);
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"ObjectiveTimerPlugin OnTick error: {ex.Message}");
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

        #region Private Methods

        private string FormatTimer(string prefix, int seconds)
        {
            var m = seconds / 60;
            var s = seconds % 60;
            return $"{prefix} {m}:{s:D2}";
        }

        #endregion
    }
}
