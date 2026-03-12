using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace LeagueDeck
{
    [PluginActionId("dev.kubo.leaguedeck.runesetup")]
    public class RuneSetupPlugin : PluginBase
    {
        // Style ID to perk ID mapping for determining primary/sub style
        private static readonly Dictionary<int, int[]> StylePerks = new Dictionary<int, int[]>
        {
            { 8000, new[] { 8005, 8008, 8021, 8010, 9101, 9111, 8009, 9104, 9105, 9103, 8014, 8017, 8299 } },
            { 8100, new[] { 8112, 8124, 8128, 9923, 8126, 8139, 8143, 8136, 8120, 8138, 8135, 8134, 8105, 8106 } },
            { 8200, new[] { 8214, 8229, 8230, 8224, 8226, 8275, 8210, 8234, 8233, 8237, 8232, 8236 } },
            { 8300, new[] { 8351, 8360, 8369, 8306, 8304, 8313, 8321, 8316, 8345, 8347, 8410, 8352 } },
            { 8400, new[] { 8437, 8439, 8465, 8446, 8463, 8401, 8429, 8444, 8473, 8451, 8453, 8242 } },
        };

        private CancellationTokenSource _cts = new CancellationTokenSource();

        private LeagueInfo _info;
        private LcuClient _lcu;

        private int _lastChampionId;
        private bool _runesApplied;
        private int _tickCounter;

        public RuneSetupPlugin(SDConnection connection, InitialPayload payload)
            : base(connection, payload)
        {
            Logger.Instance.LogMessage(TracingLevel.DEBUG, "RuneSetupPlugin - Constructor");

            LeagueInfo.OnUpdateStarted += LeagueInfo_OnUpdateStarted;
            LeagueInfo.OnUpdateProgress += LeagueInfo_OnUpdateProgress;
            LeagueInfo.OnUpdateCompleted += LeagueInfo_OnUpdateCompleted;

            _info = LeagueInfo.GetInstance(_cts.Token);
            _lcu = LcuClient.Instance;

            if (_info.UpdateTask != null)
            {
                Task.Run(async () =>
                {
                    await Connection.SetImageAsync(Utilities.GetUpdateImage());
                    await _info.UpdateTask;
                    await Connection.SetDefaultImageAsync();
                    await Connection.SetTitleAsync("Runes");
                });
            }
            else
            {
                Task.Run(async () => await Connection.SetTitleAsync("Runes"));
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
            await Connection.SetTitleAsync("Runes");
        }

        #endregion

        #region Overrides

        public override async void KeyPressed(KeyPayload payload)
        {
            try
            {
                if (!_lcu.IsConnected && !_lcu.TryConnect())
                {
                    await Connection.SetTitleAsync("No\nClient");
                    return;
                }

                var champId = await _lcu.GetMyChampionId();
                if (champId <= 0)
                {
                    await Connection.SetTitleAsync("No\nChamp");
                    return;
                }

                var champion = _info.GetChampionByKey(champId);
                await Connection.SetTitleAsync($"Loading\n{champion.Name}");

                var runeData = await FetchRuneData(champId);
                if (runeData == null)
                {
                    await Connection.SetTitleAsync("No Data");
                    return;
                }

                // Delete current rune page if it's editable, then create new one
                var currentPage = await _lcu.GetCurrentRunePage();
                if (currentPage != null)
                {
                    var isEditable = currentPage["isEditable"]?.Value<bool>() ?? false;
                    if (isEditable)
                    {
                        var pageId = currentPage["id"].Value<int>();
                        await _lcu.DeleteRunePage(pageId);
                    }
                }

                var success = await _lcu.CreateRunePage(
                    $"Auto - {champion.Name}",
                    runeData.PrimaryStyleId,
                    runeData.SubStyleId,
                    runeData.SelectedPerkIds
                );

                if (success)
                {
                    _runesApplied = true;
                    await Connection.SetTitleAsync($"Done!\n{champion.Name}");
                }
                else
                {
                    await Connection.SetTitleAsync("Failed");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"RuneSetupPlugin KeyPressed error: {ex.Message}");
                await Connection.SetTitleAsync("Error");
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
                if (!_lcu.IsConnected)
                {
                    if (_lastChampionId != 0)
                    {
                        _lastChampionId = 0;
                        _runesApplied = false;
                        await Connection.SetDefaultImageAsync();
                        await Connection.SetTitleAsync("Runes");
                    }
                    return;
                }

                var phase = await _lcu.GetGameflowPhase();

                if (phase == "ChampSelect")
                {
                    var champId = await _lcu.GetMyChampionId();

                    if (champId > 0 && champId != _lastChampionId)
                    {
                        _lastChampionId = champId;
                        _runesApplied = false;
                        var champion = _info.GetChampionByKey(champId);
                        var champImage = _info.GetChampionImage(champion.Id);
                        await Connection.SetImageAsync(champImage);
                        await Connection.SetTitleAsync($"Runes\n{champion.Name}");
                    }
                    else if (champId == 0 && _lastChampionId != 0)
                    {
                        _lastChampionId = 0;
                        _runesApplied = false;
                        await Connection.SetDefaultImageAsync();
                        await Connection.SetTitleAsync("Runes\nPick...");
                    }
                }
                else if (_lastChampionId != 0)
                {
                    _lastChampionId = 0;
                    _runesApplied = false;
                    await Connection.SetDefaultImageAsync();
                    await Connection.SetTitleAsync("Runes");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"RuneSetupPlugin OnTick error: {ex.Message}");
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

        #region Private Methods

        private async Task<RunePageData> FetchRuneData(int championId)
        {
            try
            {
                // Fetch from LoLalytics API
                var url = $"https://ax.lolalytics.com/mega/?ep=champion&p=d&v=1&patch=current&cid={championId}&lane=default&tier=emerald_plus&queue=420&region=all";

                using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "LeagueDeck/1.0");
                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.Instance.LogMessage(TracingLevel.DEBUG, $"LoLalytics API returned {response.StatusCode}");
                        return null;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    var data = JObject.Parse(json);

                    // Try highest winrate runes first, fall back to most popular
                    var runeSet = data.SelectToken("summary.runes.win.set")
                               ?? data.SelectToken("summary.runes.pick.set");

                    if (runeSet == null)
                    {
                        Logger.Instance.LogMessage(TracingLevel.DEBUG, "No rune data in LoLalytics response");
                        return null;
                    }

                    var pri = runeSet["pri"]?.Values<int>().ToArray();
                    var sec = runeSet["sec"]?.Values<int>().ToArray();
                    var mod = runeSet["mod"]?.Values<int>().ToArray();

                    if (pri == null || sec == null || mod == null || pri.Length < 4 || sec.Length < 2 || mod.Length < 3)
                    {
                        Logger.Instance.LogMessage(TracingLevel.DEBUG, "Incomplete rune data from LoLalytics");
                        return null;
                    }

                    var selectedPerkIds = pri.Concat(sec).Concat(mod).ToArray();
                    var primaryStyleId = GetStyleForPerk(pri[0]);
                    var subStyleId = GetStyleForPerk(sec[0]);

                    if (primaryStyleId == 0 || subStyleId == 0)
                    {
                        Logger.Instance.LogMessage(TracingLevel.DEBUG, "Could not determine rune style IDs");
                        return null;
                    }

                    return new RunePageData
                    {
                        PrimaryStyleId = primaryStyleId,
                        SubStyleId = subStyleId,
                        SelectedPerkIds = selectedPerkIds,
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"FetchRuneData error: {ex.Message}");
                return null;
            }
        }

        private int GetStyleForPerk(int perkId)
        {
            foreach (var kvp in StylePerks)
            {
                if (kvp.Value.Contains(perkId))
                    return kvp.Key;
            }
            return 0;
        }

        #endregion

        private class RunePageData
        {
            public int PrimaryStyleId { get; set; }
            public int SubStyleId { get; set; }
            public int[] SelectedPerkIds { get; set; }
        }
    }
}
