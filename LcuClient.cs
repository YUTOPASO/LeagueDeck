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
                    _connected = false;
                    return false;
                }

                var process = processes[0];
                var clientDir = Path.GetDirectoryName(process.MainModule.FileName);
                var lockfilePath = Path.Combine(clientDir, "lockfile");

                if (!File.Exists(lockfilePath))
                {
                    _connected = false;
                    return false;
                }

                // lockfile format: processName:PID:port:password:protocol
                string lockfileContent;
                using (var fs = new FileStream(lockfilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    lockfileContent = sr.ReadToEnd();
                }

                var parts = lockfileContent.Split(':');
                if (parts.Length < 5)
                {
                    _connected = false;
                    return false;
                }

                _port = int.Parse(parts[2]);
                _authToken = parts[3];

                _httpClient = new HttpClient();
                _httpClient.BaseAddress = new Uri($"https://127.0.0.1:{_port}");
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"riot:{_authToken}"));
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Basic", credentials);

                _connected = true;
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"LcuClient connected on port {_port}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"LcuClient TryConnect failed: {ex.Message}");
                _connected = false;
                return false;
            }
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
    }
}
