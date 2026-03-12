using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace LeagueDeck
{
    public enum EObjectiveType
    {
        Dragon = 0,
        Baron = 1,
        Herald = 2,
    }

    [PluginActionId("dev.kubo.leaguedeck.objectivetimer")]
    public class ObjectiveTimerPlugin : PluginBase
    {
        private const int DragonRespawnSeconds = 300;  // 5 min
        private const int BaronRespawnSeconds = 360;   // 6 min
        private const int HeraldRespawnSeconds = 360;  // 6 min
        private const int BlinkThreshold = 60;         // blink at 60s remaining

        private CancellationTokenSource _cts = new CancellationTokenSource();

        private LeagueInfo _info;
        private ObjectiveTimerSettings _settings;

        private bool _isInGame;
        private int _lastProcessedEventId = -1;

        private double _respawnAt = -1;
        private double _currentGameTime;
        private int _tickCounter;
        private bool _blinkOn;

        public ObjectiveTimerPlugin(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "ObjectiveTimerPlugin - Constructor");

            _info = LeagueInfo.GetInstance(_cts.Token);

            if (payload.Settings == null || !payload.Settings.HasValues)
                _settings = ObjectiveTimerSettings.CreateDefaultSettings();
            else
                _settings = payload.Settings.ToObject<ObjectiveTimerSettings>();

            Connection.OnApplicationDidLaunch += Connection_OnApplicationDidLaunch;
            Connection.OnApplicationDidTerminate += Connection_OnApplicationDidTerminate;

            Task.Run(async () =>
            {
                await Connection.SetImageAsync(Utilities.ImageToBase64(GetObjectiveIcon()));
                await Connection.SetTitleAsync(GetLabel());
            });
        }

        #region Events

        private void Connection_OnApplicationDidLaunch(object sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.ApplicationDidLaunch> e)
        {
            if (e.Event.Payload.Application != "League of Legends.exe")
                return;

            _isInGame = true;
            _lastProcessedEventId = -1;
            _respawnAt = -1;
        }

        private void Connection_OnApplicationDidTerminate(object sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.ApplicationDidTerminate> e)
        {
            if (e.Event.Payload.Application != "League of Legends.exe")
                return;

            _isInGame = false;
            _respawnAt = -1;
            Task.Run(async () => await Connection.SetTitleAsync(GetLabel()));
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

                        var targetEvent = GetTargetEventType();
                        if (evt.Type == targetEvent)
                        {
                            _respawnAt = evt.Time + GetRespawnSeconds();
                        }
                    }
                }

                // Clear expired timer
                if (_respawnAt > 0 && _currentGameTime >= _respawnAt)
                    _respawnAt = -1;

                // Display
                if (_respawnAt > 0)
                {
                    var remaining = (int)(_respawnAt - _currentGameTime);
                    if (remaining > 0)
                    {
                        if (remaining <= BlinkThreshold && !_blinkOn)
                            await Connection.SetTitleAsync(GetLabel());
                        else
                        {
                            var m = remaining / 60;
                            var s = remaining % 60;
                            await Connection.SetTitleAsync($"{GetLabel()}\n{m}:{s:D2}");
                        }
                    }
                    else
                    {
                        await Connection.SetTitleAsync(GetLabel());
                    }
                }
                else
                {
                    await Connection.SetTitleAsync(GetLabel());
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"ObjectiveTimerPlugin OnTick error: {ex.Message}");
            }
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            _settings = payload.Settings.ToObject<ObjectiveTimerSettings>();
            _respawnAt = -1;
            Task.Run(async () =>
            {
                await Connection.SetImageAsync(Utilities.ImageToBase64(GetObjectiveIcon()));
                await Connection.SetTitleAsync(GetLabel());
            });
        }

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

        private string GetLabel()
        {
            switch (_settings.ObjectiveType)
            {
                case EObjectiveType.Dragon: return "Dragon";
                case EObjectiveType.Baron: return "Baron";
                case EObjectiveType.Herald: return "Herald";
                default: return "Obj";
            }
        }

        private EEventType GetTargetEventType()
        {
            switch (_settings.ObjectiveType)
            {
                case EObjectiveType.Dragon: return EEventType.DragonKill;
                case EObjectiveType.Baron: return EEventType.BaronKill;
                case EObjectiveType.Herald: return EEventType.HeraldKill;
                default: return EEventType.DragonKill;
            }
        }

        private int GetRespawnSeconds()
        {
            switch (_settings.ObjectiveType)
            {
                case EObjectiveType.Dragon: return DragonRespawnSeconds;
                case EObjectiveType.Baron: return BaronRespawnSeconds;
                case EObjectiveType.Herald: return HeraldRespawnSeconds;
                default: return DragonRespawnSeconds;
            }
        }

        private Image GetObjectiveIcon()
        {
            switch (_settings.ObjectiveType)
            {
                case EObjectiveType.Dragon: return Utilities.LoadIcon("dragon.png") ?? Utilities.GenerateIcon("D", Color.FromArgb(200, 50, 50));
                case EObjectiveType.Baron: return Utilities.LoadIcon("baron.png") ?? Utilities.GenerateIcon("B", Color.FromArgb(130, 50, 180));
                case EObjectiveType.Herald: return Utilities.LoadIcon("herald.png") ?? Utilities.GenerateIcon("H", Color.FromArgb(50, 130, 200));
                default: return Utilities.GenerateIcon("?", Color.Gray);
            }
        }

        #endregion
    }

    public class ObjectiveTimerSettings
    {
        public static ObjectiveTimerSettings CreateDefaultSettings()
        {
            return new ObjectiveTimerSettings { ObjectiveType = EObjectiveType.Dragon };
        }

        [JsonProperty("objectiveType")]
        public EObjectiveType ObjectiveType { get; set; }
    }
}
