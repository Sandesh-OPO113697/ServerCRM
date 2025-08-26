using Microsoft.AspNetCore.SignalR;
using ServerCRM.Utils;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ServerCRM.Models.Freeswitch;
using System.Text;
using ServerCRM.FreeSwitchService;

namespace ServerCRM.FreeSwitchSer
{
    public class FreeSwitchManager
    {
        private readonly IHubContext<CallEventsHub> _hub;
        private readonly ConcurrentDictionary<string, ESLClient> _userConnections = new();
        private readonly ConcurrentDictionary<string, FsCallEvent> _activeCalls = new();
        private readonly string _fsHost;
        private readonly int _fsPort;
        private readonly string _fsPass;
        private readonly ConcurrentDictionary<string, string> _agentStatuses = new();
        public FreeSwitchManager(IHubContext<CallEventsHub> hub, IConfiguration config)
        {
            _hub = hub;
            _fsHost = "172.18.16.173";
            _fsPort = 8021;
            _fsPass = "ClueCon";
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
                    {
                        eventHeaders[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }

            var eventName = eventHeaders.GetValueOrDefault("Event-Name", "UNKNOWN");
            var callUuid = eventHeaders.GetValueOrDefault("Channel-Call-UUID", string.Empty);

            if (string.IsNullOrEmpty(callUuid))
            {
                Console.WriteLine($"Skipping event with no UUID: {eventName}");
                return;
            }

            var currentEvent = new FsCallEvent
            {
                EventName = eventName,
                Uuid = callUuid,
                ChannelState = eventHeaders.GetValueOrDefault("Channel-State", "CS_NONE"),
                CallDirection = eventHeaders.GetValueOrDefault("Call-Direction", "unknown"),
                HangupCause = eventHeaders.GetValueOrDefault("Hangup-Cause", string.Empty),
                Raw = frame
            };

            switch (currentEvent.EventName)
            {
                case "CHANNEL_CREATE":
                case "CHANNEL_OUTGOING":
                    currentEvent.Status = "Dialing";
                    break;
                case "CHANNEL_PROGRESS":
                    currentEvent.Status = "Ringing";
                    break;
                case "CHANNEL_ANSWER":
                    currentEvent.Status = "Talking";
                    break;
                case "CHANNEL_BRIDGE":
                    currentEvent.Status = "Bridged";
                    break;
                case "CHANNEL_HOLD":
                    currentEvent.Status = "On Hold";
                    break;
                case "CHANNEL_UNHOLD":
                    currentEvent.Status = "Resumed";
                    break;
                case "CHANNEL_HANGUP":
                case "CHANNEL_HANGUP_COMPLETE":
                    currentEvent.Status = "Disconnected";
                    _activeCalls.TryRemove(currentEvent.Uuid, out _);
                    break;
                default:
              
                    var channelState = currentEvent.ChannelState.ToUpper();
                    if (channelState.Contains("ACTIVE"))
                    {
                        currentEvent.Status = "Talking";
                    }
                    else if (channelState.Contains("HANGUP"))
                    {
                        currentEvent.Status = "Disconnected";
                    }
                    else if (channelState.Contains("RINGING"))
                    {
                        currentEvent.Status = "Ringing";
                    }
                    else
                    {
                       
                        if (_activeCalls.TryGetValue(currentEvent.Uuid, out var existingEvent))
                        {
                            currentEvent.Status = existingEvent.Status;
                        }
                    }
                    break;
            }

            if (currentEvent.Status != "Disconnected")
            {
                _activeCalls.AddOrUpdate(currentEvent.Uuid, currentEvent, (key, existingVal) => currentEvent);
            }

            _hub.Clients.All.SendAsync("OnFsEvent", currentEvent);
        }

        public async Task<ESLClient> GetOrCreateConnectionAsync(string userId)
        {
            if (_userConnections.TryGetValue(userId, out var existingClient) && existingClient.IsConnected)
            {
                return existingClient;
            }

            var newClient = new ESLClient(_fsHost, _fsPort, _fsPass);
            await newClient.ConnectAsync();

            newClient.OnEventReceived += ProcessFreeSwitchEvent;

            await newClient.StartEventListenerAsync();

            _userConnections.AddOrUpdate(userId, newClient, (key, existingVal) => newClient);
            return newClient;
        }

        public async Task<string?> MakeCallAsync(string userId, string gateway, string callerId, string phoneNumber)
        {
            var client = await GetOrCreateConnectionAsync(userId);
            string originateCmd = $"api originate {{origination_caller_id_name={callerId},origination_caller_id_number={callerId}}}sofia/gateway/{gateway}/{phoneNumber} &bridge(user/{callerId}@{_fsHost})\n\n";

            var result = await client.SendCommandAsync(originateCmd);
            //var match = Regex.Match(result ?? "", @"Channel-Call-UUID:\s*(\S+)", RegexOptions.IgnoreCase);

            //if (match.Success)
            //{
            //    var newUuid = match.Groups[1].Value;
            //    _activeCalls[newUuid] = new FsCallEvent { Uuid = newUuid, Status = "Dialing", Raw = result };
            //    return newUuid;
            //}

            //return null;

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

        public async Task HoldCallAsync(string userId, string callUuid)
        {
            var client = await GetOrCreateConnectionAsync(userId);
            var response = await client.SendCommandAsync($"api uuid_hold {callUuid} on");

            if (!string.IsNullOrEmpty(response) && response.Contains("+OK"))
            {
                Console.WriteLine($"Call {callUuid} successfully put on hold.");
            }
            else
            {
                Console.WriteLine($"Failed to put call {callUuid} on hold. Response: {response}");
            }
        }

        public async Task UnholdCallAsync(string userId, string callUuid)
        {
            var client = await GetOrCreateConnectionAsync(userId);
            var response = await client.SendCommandAsync($"api uuid_hold {callUuid} off");

            if (!string.IsNullOrEmpty(response) && response.Contains("+OK"))
            {
                Console.WriteLine($"Call {callUuid} successfully taken off hold.");
            }
            else
            {
                Console.WriteLine($"Failed to unhold call {callUuid}. Response: {response}");
            }
        }
        public void SetAgentStatus(string userId, string status)
        {
           
            if (status != "Ready" && status != "NotReady" && status != "Break")
            {
                Console.WriteLine($"Invalid status for user {userId}: {status}");
                return;
            }

            _agentStatuses.AddOrUpdate(userId, status, (key, existingVal) => status);
            Console.WriteLine($"Agent {userId} status changed to: {status}");
        }

        public async Task<bool> HangupCallAsync(string callUuid)
        {
            if (string.IsNullOrWhiteSpace(callUuid))
            {
                Console.WriteLine("Hangup request failed: Provided call UUID is null or empty.");
                return false;
            }

            if (!_activeCalls.ContainsKey(callUuid))
            {
                Console.WriteLine($"Hangup failed: Call with UUID {callUuid} is no longer active or was never active.");
                return false;
            }

            try
            {
                Console.WriteLine($"Attempting to hang up call with UUID: {callUuid}");

              
                var client = new ESLClient(_fsHost, _fsPort, _fsPass);
                await client.ConnectAsync();
                var response = await client.SendCommandAsync($"api uuid_kill {callUuid} NORMAL_CLEARING");


                if (response != null && response.Contains("+OK"))
                {
                    Console.WriteLine($"Successfully sent hangup command for UUID {callUuid}.");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Hangup failed for UUID {callUuid}. Response from FreeSWITCH: {response}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while trying to hang up call {callUuid}: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> MergeToConferenceAsync(string userId, string callUuid, string conferenceName)
        {
            var client = await GetOrCreateConnectionAsync(userId);
            
            var response = await client.SendCommandAsync($"api uuid_bridge {callUuid} &conference({conferenceName})");

            if (response != null && response.Contains("+OK"))
            {
                Console.WriteLine($"Successfully merged call {callUuid} into conference {conferenceName}.");
                return true;
            }
            else
            {
                Console.WriteLine($"Failed to merge call {callUuid} to conference {conferenceName}. Response: {response}");
                return false;
            }
        }
        public async Task CreateConferenceWithNumberAsync(string userId, string conferenceName, string phoneNumber, string callerId, string gateway)
        {
            var client = await GetOrCreateConnectionAsync(userId);
            string fullNumber = phoneNumber.StartsWith("7530") ? phoneNumber : "7530" + phoneNumber;
            string cmd = $"api originate {{origination_caller_id_name={callerId},origination_caller_id_number={callerId}}}sofia/gateway/{gateway}/{fullNumber} &conference({conferenceName})";
            await client.SendCommandAsync(cmd);
        }

        public async Task RemoveFromConferenceAsync(string userId, string conferenceName, string callerUuid)
        {
            var client = await GetOrCreateConnectionAsync(userId);
            await client.SendCommandAsync($"api conference {conferenceName} kick {callerUuid}");
        }
    }
}