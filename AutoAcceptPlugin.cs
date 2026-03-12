using BarRaider.SdTools;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace LeagueDeck
{
    [PluginActionId("dev.kubo.leaguedeck.autoaccept")]
    public class AutoAcceptPlugin : PluginBase
    {
        private LcuClient _lcu;
        private bool _enabled = true;
        private int _tickCounter;

        public AutoAcceptPlugin(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "AutoAcceptPlugin - Constructor");
            _lcu = LcuClient.Instance;

            Task.Run(async () => await Connection.SetTitleAsync("Auto\nAccept"));
        }

        public override async void KeyPressed(KeyPayload payload)
        {
            _enabled = !_enabled;
            await Connection.SetTitleAsync(_enabled ? "Auto\nAccept" : "Auto\nOFF");
        }

        public override void KeyReleased(KeyPayload payload) { }

        public override async void OnTick()
        {
            if (!_enabled)
                return;

            _tickCounter++;
            if (_tickCounter % 2 != 0)
                return;

            try
            {
                if (!_lcu.IsConnected && !_lcu.TryConnect())
                    return;

                var phase = await _lcu.GetGameflowPhase();
                if (phase != "ReadyCheck")
                    return;

                var playerState = await _lcu.GetPlayerState();
                if (playerState == "Accepted")
                    return;

                var success = await _lcu.AcceptReadyCheck();
                if (success)
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, "AutoAcceptPlugin - Auto accepted");
                    await Connection.SetTitleAsync("Accepted!");
                    await Task.Delay(2000);
                    await Connection.SetTitleAsync("Auto\nAccept");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"AutoAcceptPlugin OnTick error: {ex.Message}");
            }
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload) { }
        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload) { }

        public override void Dispose() { }
    }
}
