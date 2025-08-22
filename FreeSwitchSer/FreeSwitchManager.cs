
using Microsoft.AspNetCore.SignalR;
using ServerCRM.FreeSwitchService;
using ServerCRM.Models;
using ServerCRM.Utils;
using System.Text.RegularExpressions;

namespace ServerCRM.FreeSwitchSer
{
    public class FreeSwitchManager
    {
        private readonly IHubContext<CallEventsHub> _hub;
        private readonly Dictionary<string, ESLClient> _userConnections = new();
        private readonly object _lock = new();
        private readonly string _fsHost;
        private readonly int _fsPort;
        private readonly string _fsPass;

        public FreeSwitchManager(IHubContext<CallEventsHub> hub, IConfiguration config)
        {
            _hub = hub;
            _fsHost = "172.18.16.173";
            _fsPort = 8021;
            _fsPass = "ClueCon";
        }

        private FsCallEvent ParseFsEvent(string frame)
        {
            var ev = new FsCallEvent { Raw = frame };


            var uuidMatch = Regex.Match(frame, @"Channel-Call-UUID:\s*(\S+)", RegexOptions.IgnoreCase);
            if (uuidMatch.Success) ev.Uuid = uuidMatch.Groups[1].Value;
            if (frame.Contains("CHANNEL_CREATE", StringComparison.OrdinalIgnoreCase)) ev.Status = "Dialing";
            else if (frame.Contains("CHANNEL_PROGRESS", StringComparison.OrdinalIgnoreCase)) ev.Status = "Ringing";
            else if (frame.Contains("CHANNEL_ANSWER", StringComparison.OrdinalIgnoreCase)) ev.Status = "Talking";
            else if (frame.Contains("CHANNEL_BRIDGE", StringComparison.OrdinalIgnoreCase)) ev.Status = "Bridged";
            else if (frame.Contains("CHANNEL_HOLD", StringComparison.OrdinalIgnoreCase)) ev.Status = "On Hold";
            else if (frame.Contains("CHANNEL_UNHOLD", StringComparison.OrdinalIgnoreCase)) ev.Status = "Resumed";
            else if (frame.Contains("CHANNEL_HANGUP", StringComparison.OrdinalIgnoreCase)) ev.Status = "Disconnected";

            return ev;
        }

        public async Task<ESLClient> GetOrCreateConnectionAsync(string userId, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(userId)) userId = Guid.NewGuid().ToString();

            lock (_lock)
            {
                if (_userConnections.ContainsKey(userId))
                    return _userConnections[userId];
            }

            var client = new ESLClient(_fsHost, _fsPort, _fsPass);

            client.OnEventReceived += async (frame) =>
            {
                var ev = ParseFsEvent(frame);

              
                var message = new
                {
                    Uuid = ev.Uuid,
                    Status = ev.Status,
                    RawEvent = ev.Raw
                };

               
                await _hub.Clients.Group(userId).SendAsync("OnFsEvent", message);
            };

            await client.ConnectAsync(ct);
            await client.StartEventListenerAsync(ct);

            lock (_lock)
            {
                _userConnections[userId] = client;
            }

            return client;

        }

        public async Task<string?> MakeCallAsync(string userId, string gateway, string callerId, string phoneNumber, CancellationToken ct = default)
        {
            var client = await GetOrCreateConnectionAsync(userId, ct);

            string originateCmd =
                $"api originate {{origination_caller_id_name={callerId},origination_caller_id_number={callerId}}}" +
                $"sofia/gateway/{gateway}/{phoneNumber} &bridge(user/{callerId}@{_fsHost})\n\n";

            var result = await client.SendCommandAsync(originateCmd, ct);

            var match = Regex.Match(result ?? "", @"Channel-Call-UUID:\s*(\S+)", RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value;

            return null;
        }

        public async Task HoldCallAsync(string userId, string callUuid, CancellationToken ct = default)
        {
            var client = await GetOrCreateConnectionAsync(userId, ct);
            await client.SendCommandAsync($"uuid_hold {callUuid} on", ct);
        }

        public async Task UnholdCallAsync(string userId, string callUuid, CancellationToken ct = default)
        {
            var client = await GetOrCreateConnectionAsync(userId, ct);
            await client.SendCommandAsync($"uuid_hold {callUuid} off", ct);
        }

        public async Task HangupCallAsync(string userId, string callUuid, CancellationToken ct = default)
        {
            var client = await GetOrCreateConnectionAsync(userId, ct);
            await client.SendCommandAsync($"uuid_kill {callUuid}", ct);
        }

        public async Task CreateConferenceWithNumberAsync(string userId, string conferenceName, string phoneNumber, string callerId, string gateway, CancellationToken ct = default)
        {
            var client = await GetOrCreateConnectionAsync(userId, ct);
            string fullNumber = phoneNumber.StartsWith("7530") ? phoneNumber : "7530" + phoneNumber;
            string cmd = $"api originate {{origination_caller_id_name={callerId},origination_caller_id_number={callerId}}}sofia/gateway/{gateway}/{fullNumber} &conference({conferenceName})";
            await client.SendCommandAsync(cmd, ct);
        }

        public async Task RemoveFromConferenceAsync(string userId, string conferenceName, string callerUuid, CancellationToken ct = default)
        {
            var client = await GetOrCreateConnectionAsync(userId, ct);
            await client.SendCommandAsync($"api conference {conferenceName} kick {callerUuid}", ct);
        }
    }
}
