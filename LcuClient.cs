using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LeagueDeck
{
    public class LcuClient
    {
        private static LcuClient _instance;

        private HttpClient _httpClient;
        private int _port;
        private string _authToken;
        private bool _connected;

        public bool IsConnected => _connected;

        public static LcuClient Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new LcuClient();
                return _instance;
            }
        }

        private LcuClient()
        {
            ServicePointManager.ServerCertificateValidationCallback +=
                (sender, cert, chain, sslPolicyErrors) => true;
        }

        public bool TryConnect()
        {
            try
            {
                var processes = Process.GetProcessesByName("LeagueClientUx");
                if (processes.Length == 0)
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, "LcuClient - LeagueClientUx process not found");
                    _connected = false;
                    return false;
                }

                // Try command line args first (works across 32/64-bit process boundary)
                if (TryConnectFromCommandLine(processes[0].Id))
                    return true;

                // Fallback: try lockfile via MainModule path
                if (TryConnectFromLockfile(processes[0]))
                    return true;

                _connected = false;
                return false;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"LcuClient TryConnect failed: {ex.Message}");
                _connected = false;
                return false;
            }
        }

        private bool TryConnectFromCommandLine(int processId)
        {
            try
            {
                // Use wmic to get command line (works from 32-bit process to 64-bit process)
                var psi = new ProcessStartInfo("wmic",
                    $"process where processid={processId} get commandline /format:list")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                string output;
                using (var proc = Process.Start(psi))
                {
                    output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);
                }

                if (string.IsNullOrEmpty(output))
                    return false;

                var portMatch = Regex.Match(output, @"--app-port=(\d+)");
                var authMatch = Regex.Match(output, @"--remoting-auth-token=([^\s""]+)");

                if (!portMatch.Success || !authMatch.Success)
                {
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, "LcuClient - Could not parse port/token from command line");
                    return false;
                }

                _port = int.Parse(portMatch.Groups[1].Value);
                _authToken = authMatch.Groups[1].Value;

                SetupHttpClient();
                _connected = true;
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"LcuClient connected via cmdline on port {_port}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"LcuClient TryConnectFromCommandLine failed: {ex.Message}");
                return false;
            }
        }

        private bool TryConnectFromLockfile(Process process)
        {
            try
            {
                var clientDir = Path.GetDirectoryName(process.MainModule.FileName);
                var lockfilePath = Path.Combine(clientDir, "lockfile");

                if (!File.Exists(lockfilePath))
                    return false;

                string lockfileContent;
                using (var fs = new FileStream(lockfilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    lockfileContent = sr.ReadToEnd();
                }

                var parts = lockfileContent.Split(':');
                if (parts.Length < 5)
                    return false;

                _port = int.Parse(parts[2]);
                _authToken = parts[3];

                SetupHttpClient();
                _connected = true;
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"LcuClient connected via lockfile on port {_port}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"LcuClient TryConnectFromLockfile failed: {ex.Message}");
                return false;
            }
        }

        private void SetupHttpClient()
        {
            _httpClient?.Dispose();
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri($"https://127.0.0.1:{_port}");
            var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{_authToken}"));
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
        }

        public void Disconnect()
        {
            _httpClient?.Dispose();
            _httpClient = null;
            _connected = false;
        }

        public async Task<string> GetGameflowPhase()
        {
            if (!_connected)
                return null;

            try
            {
                var response = await _httpClient.GetAsync("/lol-gameflow/v1/gameflow-phase");
                if (!response.IsSuccessStatusCode)
                    return null;

                var content = await response.Content.ReadAsStringAsync();
                // Response is a quoted string like "ChampSelect"
                return content.Trim('"');
            }
            catch
            {
                return null;
            }
        }

        public async Task<JObject> GetChampSelectSession()
        {
            if (!_connected)
                return null;

            try
            {
                var response = await _httpClient.GetAsync("/lol-champ-select/v1/session");
                if (!response.IsSuccessStatusCode)
                    return null;

                var content = await response.Content.ReadAsStringAsync();
                return JObject.Parse(content);
            }
            catch
            {
                return null;
            }
        }

        public async Task<int> GetMyChampionId()
        {
            var session = await GetChampSelectSession();
            if (session == null)
                return 0;

            var localCellId = session["localPlayerCellId"]?.Value<int>() ?? -1;
            var myTeam = session["myTeam"] as JArray;
            if (myTeam == null)
                return 0;

            foreach (var member in myTeam)
            {
                var cellId = member["cellId"]?.Value<int>() ?? -1;
                if (cellId == localCellId)
                    return member["championId"]?.Value<int>() ?? 0;
            }

            return 0;
        }

        public async Task<JObject> GetCurrentRunePage()
        {
            if (!_connected)
                return null;

            try
            {
                var response = await _httpClient.GetAsync("/lol-perks/v1/currentpage");
                if (!response.IsSuccessStatusCode)
                    return null;

                var content = await response.Content.ReadAsStringAsync();
                return JObject.Parse(content);
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> DeleteRunePage(int pageId)
        {
            if (!_connected)
                return false;

            try
            {
                var response = await _httpClient.DeleteAsync($"/lol-perks/v1/pages/{pageId}");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> CreateRunePage(string name, int primaryStyleId, int subStyleId, int[] selectedPerkIds)
        {
            if (!_connected)
                return false;

            try
            {
                var body = new JObject
                {
                    ["name"] = name,
                    ["primaryStyleId"] = primaryStyleId,
                    ["subStyleId"] = subStyleId,
                    ["selectedPerkIds"] = new JArray(selectedPerkIds),
                    ["current"] = true
                };

                var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/lol-perks/v1/pages", content);

                if (!response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    Logger.Instance.LogMessage(TracingLevel.DEBUG, $"CreateRunePage failed: {response.StatusCode} - {responseBody}");
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"CreateRunePage error: {ex.Message}");
                return false;
            }
        }
        public async Task<bool> AcceptReadyCheck()
        {
            if (!_connected)
                return false;

            try
            {
                var content = new StringContent("", Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("/lol-matchmaking/v1/ready-check/accept", content);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GetReadyCheckState()
        {
            if (!_connected)
                return null;

            try
            {
                var response = await _httpClient.GetAsync("/lol-matchmaking/v1/ready-check");
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);
                return data["state"]?.Value<string>();
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> GetPlayerState()
        {
            if (!_connected)
                return null;

            try
            {
                var response = await _httpClient.GetAsync("/lol-matchmaking/v1/ready-check");
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);
                return data["playerResponse"]?.Value<string>();
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> PickChampion(int championId)
        {
            if (!_connected)
                return false;

            try
            {
                var session = await GetChampSelectSession();
                if (session == null)
                    return false;

                var localCellId = session["localPlayerCellId"]?.Value<int>() ?? -1;
                var actions = session["actions"] as JArray;
                if (actions == null)
                    return false;

                foreach (JArray actionGroup in actions)
                {
                    foreach (var action in actionGroup)
                    {
                        var actorCellId = action["actorCellId"]?.Value<int>() ?? -1;
                        var type = action["type"]?.Value<string>();
                        var isInProgress = action["isInProgress"]?.Value<bool>() ?? false;
                        var completed = action["completed"]?.Value<bool>() ?? false;

                        if (actorCellId == localCellId && type == "pick" && isInProgress && !completed)
                        {
                            var actionId = action["id"].Value<int>();
                            var body = new JObject
                            {
                                ["championId"] = championId,
                                ["completed"] = true
                            };
                            var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

                            var request = new HttpRequestMessage(new HttpMethod("PATCH"),
                                $"/lol-champ-select/v1/session/actions/{actionId}");
                            request.Content = content;
                            var response = await _httpClient.SendAsync(request);
                            return response.IsSuccessStatusCode;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"PickChampion error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> BanChampion(int championId)
        {
            if (!_connected)
                return false;

            try
            {
                var session = await GetChampSelectSession();
                if (session == null)
                    return false;

                var localCellId = session["localPlayerCellId"]?.Value<int>() ?? -1;
                var actions = session["actions"] as JArray;
                if (actions == null)
                    return false;

                foreach (JArray actionGroup in actions)
                {
                    foreach (var action in actionGroup)
                    {
                        var actorCellId = action["actorCellId"]?.Value<int>() ?? -1;
                        var type = action["type"]?.Value<string>();
                        var isInProgress = action["isInProgress"]?.Value<bool>() ?? false;
                        var completed = action["completed"]?.Value<bool>() ?? false;

                        if (actorCellId == localCellId && type == "ban" && isInProgress && !completed)
                        {
                            var actionId = action["id"].Value<int>();
                            var body = new JObject
                            {
                                ["championId"] = championId,
                                ["completed"] = true
                            };
                            var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

                            var request = new HttpRequestMessage(new HttpMethod("PATCH"),
                                $"/lol-champ-select/v1/session/actions/{actionId}");
                            request.Content = content;
                            var response = await _httpClient.SendAsync(request);
                            return response.IsSuccessStatusCode;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"BanChampion error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SetSummonerSpells(int spell1Id, int spell2Id)
        {
            if (!_connected)
                return false;

            try
            {
                var body = new JObject
                {
                    ["spell1Id"] = spell1Id,
                    ["spell2Id"] = spell2Id
                };
                var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(new HttpMethod("PATCH"),
                    "/lol-champ-select/v1/session/my-selection");
                request.Content = content;
                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"SetSummonerSpells error: {ex.Message}");
                return false;
            }
        }

        public async Task<JObject> GetRankedStats()
        {
            if (!_connected)
                return null;

            try
            {
                var response = await _httpClient.GetAsync("/lol-ranked/v1/current-ranked-stats");
                if (!response.IsSuccessStatusCode)
                    return null;

                var content = await response.Content.ReadAsStringAsync();
                return JObject.Parse(content);
            }
            catch
            {
                return null;
            }
        }

        public async Task<long> GetCurrentSummonerId()
        {
            if (!_connected)
                return 0;

            try
            {
                var response = await _httpClient.GetAsync("/lol-summoner/v1/current-summoner");
                if (!response.IsSuccessStatusCode)
                    return 0;

                var content = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(content);
                return data["summonerId"]?.Value<long>() ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
