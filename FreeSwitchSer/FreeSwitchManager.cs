using Microsoft.AspNetCore.SignalR;
using ServerCRM.Utils;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ServerCRM.Models.Freeswitch;
using System.Text;
using ServerCRM.FreeSwitchService;
using Genesyslab.InteropServices;
using ServerCRM.Services;
using Genesyslab.Platform.Voice.Protocols.PreviewInteraction;

namespace ServerCRM.FreeSwitchSer
{
    public class FreeSwitchManager
    {
        private readonly IHubContext<CallEventsHub> _hub;
        private readonly ConcurrentDictionary<string, ESLClient> _userConnections = new();
        private readonly ConcurrentDictionary<string, FsCallEvent> _activeCalls = new();
        private readonly ConcurrentDictionary<string, UserCallInfo> _userCallInfos = new();
        private readonly string _fsHost;
        private readonly int _fsPort;
        private readonly string _fsPass;
        public string logincodedn;
        private readonly ConcurrentDictionary<string, string> _agentStatuses = new();

        public FreeSwitchManager(IHubContext<CallEventsHub> hub, IConfiguration config)
        {
            _hub = hub;
            _fsHost = "172.18.16.173";
            _fsPort = 8021;
            _fsPass = "ClueCon";
        }

        private void UpdateUserCallInfo(string loginCode, string callerId, string leg1Uuid, string leg2Uuid, string status,
            string conferenceName, string conferenceNumber, string confLeg1Uuid, string confLeg2Uuid)
        {
            var info = _userCallInfos.GetOrAdd(loginCode, new UserCallInfo { LoginCode = loginCode, CallerId = callerId });

            if (!string.IsNullOrEmpty(leg1Uuid)) info.Leg1Uuid = leg1Uuid;
            if (!string.IsNullOrEmpty(leg2Uuid)) info.Leg2Uuid = leg2Uuid;
            if (!string.IsNullOrEmpty(status)) info.Status = status;
            if (!string.IsNullOrEmpty(conferenceName)) info.conferenceName = conferenceName;
            if (!string.IsNullOrEmpty(conferenceNumber)) info.conferenceNumber = conferenceNumber;
            if (!string.IsNullOrEmpty(confLeg1Uuid)) info.ConfLeg1Uuid = confLeg1Uuid;
            if (!string.IsNullOrEmpty(confLeg2Uuid)) info.ConfLeg2Uuid = confLeg2Uuid;
        }

        public void ProcessFreeSwitchEvent(string frame)
        {
            var eventHeaders = new Dictionary<string, string>();
            using (var reader = new StringReader(frame))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var parts = line.Split(new[] { ':' }, 2);
                    if (parts.Length == 2)
                        eventHeaders[parts[0].Trim()] = parts[1].Trim();
                }
            }

            var eventName = eventHeaders.GetValueOrDefault("Event-Name", "UNKNOWN");
            string status = "";
            string? callUuid = eventHeaders.GetValueOrDefault("Channel-Call-UUID", string.Empty) ?? eventHeaders.GetValueOrDefault("Unique-ID", string.Empty);
            string? callerId = eventHeaders.GetValueOrDefault("Caller-Caller-ID-Number", "");
            string? loginCode = eventHeaders.GetValueOrDefault("variable_login_code", string.Empty);

            if (string.IsNullOrEmpty(callUuid)) return;

            LogToFile($"Event Name: {eventName} | UUID: {callUuid} | CallerId: {callerId} | LoginCode: {loginCode}", "FreeSwitchEvents");

            string normalLeg1Uuid = string.Empty;
            string normalLeg2Uuid = string.Empty;
            string conferenceName = string.Empty;
            string conferenceNumber = string.Empty;
            string confLeg1Uuid = string.Empty;
            string confLeg2Uuid = string.Empty;

            switch (eventName)
            {
                case "CHANNEL_CREATE":
                case "CHANNEL_OUTGOING":
                    status = AgentStatusMapper.StatusMap[2];
                    normalLeg1Uuid = eventHeaders.GetValueOrDefault("Unique-ID", "");
                    normalLeg2Uuid = eventHeaders.GetValueOrDefault("Other-Leg-Unique-ID", "");
                    break;

                case "CHANNEL_PROGRESS":
                case "CHANNEL_PROGRESS_MEDIA":
                    status = AgentStatusMapper.StatusMap[2];
                    break;

                case "CHANNEL_ANSWER":
                case "CHANNEL_BRIDGE":
                    status = AgentStatusMapper.StatusMap[3];
                    normalLeg1Uuid = eventHeaders.GetValueOrDefault("Unique-ID", "");
                    normalLeg2Uuid = eventHeaders.GetValueOrDefault("Other-Leg-Unique-ID", "");
                    break;

                case "CHANNEL_UNBRIDGE":
                    status = "Unbridged";
                    break;

                case "CHANNEL_HOLD":
                    status = AgentStatusMapper.StatusMap[10];
                    break;

                case "CHANNEL_UNHOLD":
                    status = AgentStatusMapper.StatusMap[3];
                    break;

                case "CHANNEL_HANGUP":
                case "CHANNEL_HANGUP_COMPLETE":
                case "CHANNEL_DESTROY":
                    status = AgentStatusMapper.StatusMap[4];
                    break;

                case "CONFERENCE_JOIN":
                    status = "Conference Join";
                    confLeg1Uuid = eventHeaders.GetValueOrDefault("Unique-ID", "");
                    confLeg2Uuid = eventHeaders.GetValueOrDefault("Other-Leg-Unique-ID", "");
                    conferenceName = eventHeaders.GetValueOrDefault("Conference-Name", "");
                    conferenceNumber = eventHeaders.GetValueOrDefault("Conference-Number", "");
                    break;

                case "CONFERENCE_LEAVE":
                    status = "Conference Leave";
                    break;

                case "CONFERENCE_MUTE":
                    status = "Conference Mute";
                    break;

                case "CONFERENCE_UNMUTE":
                    status = "Conference Unmute";
                    break;

                case "CONFERENCE_DESTROY":
                    status = "Conference End";
                    break;

                default:
                    status = eventHeaders.GetValueOrDefault("Channel-State", "CS_UNKNOWN") switch
                    {
                        "CS_NEW" => AgentStatusMapper.StatusMap[1],
                        "CS_INIT" => AgentStatusMapper.StatusMap[1],
                        "CS_ROUTING" => AgentStatusMapper.StatusMap[2],
                        "CS_SOFT_EXECUTE" => AgentStatusMapper.StatusMap[13],
                        "CS_EXECUTE" => AgentStatusMapper.StatusMap[3],
                        "CS_HIBERNATE" or "CS_RESET" or "CS_DESTROY" => AgentStatusMapper.StatusMap[4],
                        "CS_HOLD" => AgentStatusMapper.StatusMap[10],
                        _ => AgentStatusMapper.StatusMap[0]
                    };
                    break;
            }

            if (!string.IsNullOrEmpty(logincodedn))
            {
                UpdateUserCallInfo(logincodedn, callerId, normalLeg1Uuid, normalLeg2Uuid, status, conferenceName, conferenceNumber, confLeg1Uuid, confLeg2Uuid);
            }

            _activeCalls.AddOrUpdate(callUuid, new FsCallEvent
            {
                Uuid = callUuid,
                EventName = eventName,
                Status = status,
                Raw = frame,
                CallerId = callerId
            }, (key, existing) =>
            {
                existing.Status = status;
                return existing;
            });

            _hub.Clients.All.SendAsync("OnFsEvent", new
            {
                Uuid = callUuid,
                Status = status
            });
        }

        public async Task<UserCallInfo?> GetUserCallInfoAsync(string loginCode)
        {
            _userCallInfos.TryGetValue(loginCode, out var info);
            return info;
        }

        public UserCallInfo GetUserCallInfoAsync2(string loginCode)
        {
            _userCallInfos.TryGetValue(loginCode, out var info);
            return info;
        }

        private static void LogToFile(string text, string AgentID)
        {
            string logFilePath = @"D:\Logs\" + AgentID + "ServerCRM_Log.txt";

            try
            {
                string directoryPath = Path.GetDirectoryName(logFilePath);
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                using (StreamWriter writer = new StreamWriter(logFilePath, append: true))
                {
                    writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}");
                    writer.WriteLine("--------------------------------------------------");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to write to log: {ex.Message}");
            }
        }

        public async Task<ESLClient> GetOrCreateConnectionAsync(string loginCode)
        {
            if (_userConnections.TryGetValue(loginCode, out var existingClient) && existingClient.IsConnected)
                return existingClient;

            var newClient = new ESLClient(_fsHost, _fsPort, _fsPass);
            await newClient.ConnectAsync();
            newClient.OnEventReceived += ProcessFreeSwitchEvent;
            await newClient.StartEventListenerAsync();

            _userConnections.AddOrUpdate(loginCode, newClient, (key, val) => newClient);
            return newClient;
        }

        public async Task<string?> MakeCallAsync(string userId, string gateway, string callerId, string phoneNumber)
        {
            var client = await GetOrCreateConnectionAsync(userId);
            string originateCmd = $"api originate {{origination_caller_id_name={callerId},origination_caller_id_number={callerId}}}sofia/gateway/{gateway}/{phoneNumber} &bridge(user/{callerId}@{_fsHost})\n\n";

            var result = await client.SendCommandAsync(originateCmd);

            var match = Regex.Match(result ?? "", @"Channel-Call-UUID:\s*(\S+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var aLegUuid = match.Groups[1].Value;
                _activeCalls[aLegUuid] = new FsCallEvent { Uuid = aLegUuid, Status = "Dialing", Raw = result };

                for (int i = 0; i < 50; i++)
                {
                    var softphoneLeg = _activeCalls.Values.FirstOrDefault(c =>
                        c.DestinationNumber == callerId && c.Status != "Disconnected");

                    if (softphoneLeg != null)
                    {
                        return softphoneLeg.Uuid;
                    }

                    await Task.Delay(100);
                }
            }

            return null;
        }

        public async Task HoldCallAsync(string loginCode)
        {
            var info = await GetUserCallInfoAsync(loginCode);
            if (info == null || string.IsNullOrEmpty(info.Leg1Uuid)) return;

            var client = await GetOrCreateConnectionAsync(loginCode);
            await client.SendCommandAsync($"api uuid_hold {info.Leg1Uuid} on");
            info.Status = "On Hold";
        }

        public async Task UnholdCallAsync(string loginCode)
        {
            var info = await GetUserCallInfoAsync(loginCode);
            if (info == null || string.IsNullOrEmpty(info.Leg1Uuid)) return;

            var client = await GetOrCreateConnectionAsync(loginCode);
            await client.SendCommandAsync($"api uuid_hold {info.Leg1Uuid} off");
            info.Status = "Talking";
        }

        public void SetuserName(string userName)
        {
            _hub.Clients.All.SendAsync("UserName", new { userName });
            _hub.Clients.All.SendAsync("OnFsEvent", new { Uuid = "", Status = "WAITING" });
        }

        public void SetAgentStatus(string userId, string status)
        {
            logincodedn = userId;
            if (status != "Ready" && status != "NotReady" && status != "Break")
            {
                Console.WriteLine($"Invalid status for user {userId}: {status}");
                return;
            }

            _agentStatuses.AddOrUpdate(userId, status, (key, existingVal) => status);
            Console.WriteLine($"Agent {userId} status changed to: {status}");
        }

        public async Task HangupCallAsync(string loginCode)
        {
            var info = await GetUserCallInfoAsync(loginCode);
            if (info == null || string.IsNullOrEmpty(info.Leg1Uuid)) return;

            var client = await GetOrCreateConnectionAsync(loginCode);
            await client.SendCommandAsync($"api uuid_kill {info.Leg1Uuid} NORMAL_CLEARING");
            info.Status = "Disconnected";
            _activeCalls.TryRemove(info.Leg1Uuid, out _);
        }

        public async Task<bool> MergeToConferenceAsync(string userId)
        {
            var client = await GetOrCreateConnectionAsync(userId);
            var info = await GetUserCallInfoAsync(userId);

            if (string.IsNullOrEmpty(info?.Leg1Uuid) || string.IsNullOrEmpty(info.Leg2Uuid))
                return false;

            if (string.IsNullOrEmpty(info.conferenceName))
                info.conferenceName = $"conf_{Guid.NewGuid()}";

            var responseLeg1 = await client.SendCommandAsync(
                $"api uuid_transfer {info.Leg1Uuid} conference:{info.conferenceName} inline"
            );
          
            return true;
        }

        public async Task CreateConferenceWithNumberAsync(string userId, string conferenceName, string phoneNumber, string callerId, string gateway)
        {
            var client = await GetOrCreateConnectionAsync(userId);
            var info = await GetUserCallInfoAsync(userId);
            string cmd = $"api originate {{origination_caller_id_name={callerId},origination_caller_id_number={callerId}}}sofia/gateway/{gateway}/{phoneNumber} &conference({conferenceName})";
            var response = await client.SendCommandAsync(cmd);
            UpdateUserCallInfo(logincodedn, callerId, info?.Leg1Uuid, info?.Leg2Uuid, null, conferenceName, conferenceName, null, null);

            var match = Regex.Match(response ?? "", @"Channel-Call-UUID:\s*(\S+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string callUuid = match.Groups[1].Value;
               
                UpdateUserCallInfo(logincodedn, callerId, info.Leg1Uuid, info.Leg2Uuid, null, conferenceName, conferenceName, callUuid, null);

            }


        }

        public async Task<bool> RemoveFromConferenceAsync(string userId)
        {
            var info = await GetUserCallInfoAsync(userId);
            var client = await GetOrCreateConnectionAsync(userId);

         
               
           
            string targetUuid = !string.IsNullOrEmpty(info.Leg1Uuid) ? info.Leg1Uuid : info.Leg1Uuid;
            if (string.IsNullOrEmpty(targetUuid))
                return false;
            var response = await client.SendCommandAsync($"api uuid_kill {info.Leg1Uuid} NORMAL_CLEARING");
            //var response = await client.SendCommandAsync(
            //    $"api conference {info.conferenceName} kick {targetUuid}"
            //);

            return response?.Contains("+OK") ?? false;
        }

        public async Task GetFreeSwitchStatusAsync(string userID)
        {
            var client = await GetOrCreateConnectionAsync(userID);
            await client.SendCommandAsync("status");
        }
    }
}
